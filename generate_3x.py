"""Generate the complete 3.x binding definitions for every Datadog module.

3.0 redistributed the whole Objective-C surface out of DatadogObjc and into the product modules,
so there is no useful delta to apply against the 2.x sources - each module's ApiDefinitions.cs and
StructsAndEnums.cs is generated from scratch here.

Objective Sharpie cannot be used: its bundled clang fails on recent iOS SDK module maps. See
build/GenerateBindings.sh.
"""

from __future__ import annotations

import pathlib
import re
import sys

sys.path.insert(0, "/private/tmp/claude-501/-Users-sb-Documents-src-my-DatadogNet-iOS/"
                   "3c7b3926-e63a-46d2-b610-015f348b6a66/scratchpad")
import genbind as g  # noqa: E402

# framework -> package name
MODULES = {
    "DatadogCore": "Core",
    "DatadogInternal": "Internal",
    "DatadogLogs": "Logs",
    "DatadogRUM": "RUM",
    "DatadogTrace": "Trace",
    "DatadogSessionReplay": "SessionReplay",
    "DatadogWebViewTracking": "WebViewTracking",
    "DatadogCrashReporting": "CrashReporting",
}

# Frameworks whose Objective-C surface is empty. They still need the file to exist.
EMPTY = {"DatadogFlags": "Flags", "DatadogProfiling": "Profiling", "OpenTelemetryApi": "OpenTelemetryApi"}

# UIKit/WebKit types that appear in signatures, so the right usings are emitted.
EXTRA_USINGS = {
    "UIView": "UIKit", "UIViewController": "UIKit", "UIImage": "UIKit", "UIApplication": "UIKit",
    "WKWebView": "WebKit", "WKUserContentController": "WebKit",
}


def framework_dir(framework: str) -> pathlib.Path:
    base = pathlib.Path("libs") / f"{framework}.xcframework"
    slices = [d for d in base.glob("ios-*") if "simulator" not in d.name]
    return slices[0] / f"{framework}.framework/Headers"


def header_text(framework: str) -> str:
    d = framework_dir(framework)
    return "\n".join(p.read_text(errors="replace") for p in sorted(d.glob("*.h")))


def is_unavailable_init(body: str) -> bool:
    """A model type declares `- init SWIFT_UNAVAILABLE`, meaning it cannot be constructed."""
    for line in body.splitlines():
        if re.match(r"^\s*-\s*\([^)]*\)\s*init\b", line):
            return "SWIFT_UNAVAILABLE" in line
    return True  # no init declared at all -> not constructible either


# Protocols and classes that are Foundation/UIKit forward declarations rather than Datadog types.
# The header declares them so its own signatures compile; binding them would collide with the SDK's
# own definitions.
SYSTEM_TYPES = {
    "NSURLSessionDataDelegate", "NSURLSessionTaskDelegate", "NSURLSessionDelegate",
    "NSURLSession", "NSURLSessionTask", "NSCoding", "NSCopying", "NSObject",
}


def split_params(rest: str) -> list[tuple[str, str, str]] | None:
    """Parse `label:(type)name label2:(type2)name2` with balanced parentheses.

    A naive `\(([^)]*)\)` stops at the first closing paren, which mangles every block parameter -
    `(void (^)(NSTimeInterval))update` is read as the type `void (^`. All ten block-taking methods
    in the SDK come out as syntactically invalid C# without this.
    """
    params = []
    i = 0
    while i < len(rest):
        m = re.compile(r"(\w+)\s*:\s*\(").search(rest, i)
        if not m:
            break
        label = m.group(1)
        depth = 1
        j = m.end()
        while j < len(rest) and depth:
            if rest[j] == "(":
                depth += 1
            elif rest[j] == ")":
                depth -= 1
            j += 1
        type_text = rest[m.end():j - 1]
        name_match = re.compile(r"\s*(\w+)").match(rest, j)
        if not name_match:
            return None
        params.append((label, type_text, name_match.group(1)))
        i = name_match.end()
    return params or None


def map_block(type_text: str) -> str | None:
    """Map an Objective-C block type to Action<...> / Func<..., TResult>."""
    # The caret can carry attributes: `void(NS_NOESCAPE ^)(void)`, `void (^ _Nonnull)(...)`.
    m = re.match(r"\s*(.+?)\s*\(\s*[^()]*\^[^()]*\)\s*\((.*)\)\s*$", type_text, re.S)
    if not m:
        return None
    ret_raw, args_raw = m.group(1), m.group(2)
    args = [a for a in (x.strip() for x in args_raw.split(",")) if a and a != "void"]
    mapped = [g.map_type(a) for a in args]
    ret = g.map_type(ret_raw)
    if ret == "void":
        return f"Action<{', '.join(mapped)}>" if mapped else "Action"
    return f"Func<{', '.join(mapped + [ret])}>" if mapped else f"Func<{ret}>"


def render_method_3x(decl: str) -> list[str] | None:
    """Render one Objective-C method, handling blocks and out-parameters."""
    m = re.match(r"\s*([-+])\s*\(([^)]*)\)\s*(.+)", decl.strip(), re.S)
    if not m:
        return None
    kind, ret_raw, rest = m.group(1), m.group(2), m.group(3)
    rest = re.sub(r"SWIFT_\w+(\([^)]*\))?", "", rest).strip().rstrip(";").strip()

    params = split_params(rest)
    if params is None:
        return g.render_method(decl)

    selector = ":".join(p[0] for p in params) + ":"
    rendered_args = []
    for _, type_text, name in params:
        block = map_block(type_text)
        if block:
            rendered_args.append(f"{block} {name}")
            continue
        # Pointer-to-pointer out-parameters. Counted rather than matched as `**` because
        # nullability annotations sit between the stars: `NSError * _Nullable * _Nullable`.
        # Generic arguments are stripped first - `NSDictionary<NSString *, id> *` has two stars but
        # is an ordinary input parameter, and binding it as `out` silently breaks every caller.
        bare = type_text
        while "<" in bare:
            stripped = re.sub(r"<[^<>]*>", "", bare)
            if stripped == bare:
                break
            bare = stripped
        if bare.count("*") >= 2:
            inner = g.map_type(re.sub(r"__autoreleasing|\*", " ", type_text))
            rendered_args.append(f"[NullAllowed] out {inner} {name}")
            continue
        prefix = "[NullAllowed] " if g.nullable(type_text) else ""
        rendered_args.append(f"{prefix}{g.map_type(type_text)} {name}")

    ret = g.map_type(ret_raw)
    lines = []
    if kind == "+":
        lines.append("\t\t[Static]")
    lines.append(f'\t\t[Export ("{selector}")]')
    # A constructor is identified by returning instancetype on an *instance* method - not by the
    # selector starting with "init". DDDatadog's `+ initializeWithConfiguration:trackingConsent:`
    # is a static void method whose name merely begins that way, and binding it as a constructor
    # produces a static constructor with parameters.
    if kind == "-" and ret == "instancetype":
        lines.append(f"\t\tNativeHandle Constructor ({', '.join(rendered_args)});")
    else:
        lines.append(f"\t\t{ret} {g.pascal(params[0][0])} ({', '.join(rendered_args)});")
    return lines


def methods_3x(body: str) -> list[list[str]]:
    out = []
    seen = set()
    for m in re.finditer(r"^\s*[-+]\s*\([^)]*\)[^;]+;", body, re.M):
        raw = m.group(0)
        if "SWIFT_UNAVAILABLE" in raw:
            continue
        key = re.sub(r"\s+", " ", raw)
        if key in seen:
            continue
        seen.add(key)
        rendered = render_method_3x(g.strip_macros(raw))
        if rendered:
            out.append(rendered)
    return out


def class_properties(body: str) -> set[str]:
    """Names of `@property (class, ...)` members.

    Swift emits these as SWIFT_CLASS_PROPERTY(@property (class ...) foo;) *and* a matching
    `+ (T)foo;` getter - the same member spelled twice. Binding both produces two C# members with
    one name (CS0102), so the property wins and the getter is skipped.
    """
    names = set()
    for m in re.finditer(r"@property\s*\(([^)]*)\)\s*[^;]+?\s*(\w+)\s*;", g.strip_macros(body)):
        if "class" in [a.strip() for a in m.group(1).split(",")]:
            names.add(m.group(2))
    return names


def selector_of(rendered: list[str]) -> str | None:
    for line in rendered:
        m = re.search(r'Export \("([^"]+)"', line)
        if m:
            return m.group(1)
    return None


def conformances(header: str, name: str) -> list[str]:
    """Protocols an @interface declares conformance to, e.g. `: NSObject <DDFoo>`."""
    m = re.search(r"@interface\s+" + re.escape(name) + r"\s*:\s*\w+\s*<([^>]+)>", header)
    if not m:
        return []
    return [p.strip() for p in m.group(1).split(",") if p.strip().startswith("DD")]


def render(framework: str, package: str) -> tuple[str, str]:
    header = header_text(framework)
    interfaces = g.interfaces(header)
    protocols = g.protocols(header)
    enums = g.enums(header)

    # Which extra frameworks the signatures need.
    body_text = header
    usings = {"System", "Foundation", "ObjCRuntime"}
    if framework != "DatadogInternal":
        # Shared types such as DDCoreLoggerLevel and DDTracingHeaderType are declared in
        # DatadogInternal and merely used here; every module links it.
        usings.add("DatadogInternal")
    for objc_type, ns in EXTRA_USINGS.items():
        if re.search(rf"\b{objc_type}\b", body_text):
            usings.add(ns)

    out: list[str] = []
    for using in sorted(usings, key=lambda u: (u != "System", u)):
        out.append(f"using {using};")
    out.append("")
    out.append(f"namespace {framework}")
    out.append("{")

    # Block typedefs used in signatures become C# delegates. Without these the generated file
    # references a type that was never declared.
    for m in re.finditer(r"typedef\s+(\w[\w\s*]*?)\s*\(\s*\^\s*(\w+)\s*\)\s*\((.*?)\)\s*;", header, re.S):
        ret_raw, name, args_raw = m.group(1), m.group(2), m.group(3)
        args = []
        for i, arg in enumerate(a.strip() for a in args_raw.split(",")):
            if not arg or arg == "void":
                continue
            words = arg.split()
            arg_name = words[-1] if words[-1].isidentifier() and not words[-1].startswith("_") else f"arg{i}"
            arg_type = arg[: arg.rfind(arg_name)] if arg_name in arg else arg
            prefix = "[NullAllowed] " if g.nullable(arg) else ""
            args.append(f"{prefix}{g.map_type(arg_type)} {arg_name}")
        out.append(f"\t// {m.group(0).split(chr(10))[0].strip()}")
        out.append(f"\tdelegate {g.map_type(ret_raw)} {name} ({', '.join(args)});")
        out.append("")

    # Protocols first: classes reference their generated I<Name> interfaces.
    for name in sorted(protocols):
        if name in SYSTEM_TYPES:
            continue
        mangled, body = protocols[name]
        out.append(f"\tpartial interface I{name} {{}}")
        out.append("")
        out.append(f"\t// @protocol {name}")
        out.append(f'\t[Model, Protocol (Name = "{mangled}")]' if mangled else "\t[Model, Protocol]")
        out.append("\t[BaseType (typeof(NSObject))]")
        out.append(f"\tinterface {name}")
        out.append("\t{")
        statics = class_properties(body)
        for sel, cs_type, attrs, readonly, raw in g.properties(body):
            out.append("\t\t[Abstract]")
            if sel in statics:
                out.append("\t\t[Static]")
            out.extend(g.render_property(sel, cs_type, attrs, readonly, raw))
            out.append("")
        for rendered in methods_3x(body):
            if selector_of(rendered) in statics:
                continue
            out.append("\t\t[Abstract]")
            out.extend(rendered)
            out.append("")
        if out[-1] == "":
            out.pop()
        out.append("\t}")
        out.append("")

    for name in sorted(interfaces):
        if name in SYSTEM_TYPES:
            continue
        mangled, body = interfaces[name]

        # A category on a foreign class (UIView) is bound as [Category], not as a type.
        if re.search(r"@interface\s+" + re.escape(name) + r"\s*\(", header) and name not in ("NSObject",):
            if name in EXTRA_USINGS:
                out.append(f"\t// @interface {name} (SWIFT_EXTENSION({framework}))")
                out.append("\t[Category]")
                out.append(f"\t[BaseType (typeof({name}))]")
                out.append(f"\tinterface {name}_{framework}")
                out.append("\t{")
                for sel, cs_type, attrs, readonly, raw in g.properties(body):
                    out.append(f'\t\t[Export ("{sel}")]')
                    out.append(f"\t\t{cs_type} Get{g.pascal(sel)} ();")
                    out.append("")
                if out[-1] == "":
                    out.pop()
                out.append("\t}")
                out.append("")
                continue

        conforms = conformances(header, name)
        out.append(f"\t// @interface {name}")
        if mangled:
            out.append(f'\t[BaseType (typeof(NSObject), Name = "{mangled}")]')
        else:
            # SWIFT_CLASS_NAMED sets only swift_name; the runtime name is the @interface name, so
            # no Name= is emitted. Getting this wrong compiles and then fails at runtime with
            # "the native class hasn't been loaded".
            out.append("\t[BaseType (typeof(NSObject))]")
        if is_unavailable_init(body):
            out.append("\t[DisableDefaultCtor]")
        suffix = " : " + ", ".join(conforms) if conforms else ""
        out.append(f"\tinterface {name}{suffix}")
        out.append("\t{")
        statics = class_properties(body)
        for sel, cs_type, attrs, readonly, raw in g.properties(body):
            if sel in statics:
                out.append("\t\t[Static]")
            out.extend(g.render_property(sel, cs_type, attrs, readonly, raw))
            out.append("")
        for rendered in methods_3x(body):
            if selector_of(rendered) in statics:
                continue
            out.extend(rendered)
            out.append("")
        if out[-1] == "":
            out.pop()
        out.append("\t}")
        out.append("")

    if out[-1] == "":
        out.pop()
    out.append("}")

    enum_out = ["using ObjCRuntime;", "", f"namespace {framework}", "{"]
    for name in sorted(enums):
        members = enums[name]
        enum_out.append("\t[Native]")
        enum_out.append(f"\tpublic enum {name} : long")
        enum_out.append("\t{")
        for i, (member, value) in enumerate(members):
            member_name = member if member[:1].isalpha() else "_" + member
            enum_out.append(f"\t\t{member_name} = {value}" + ("," if i < len(members) - 1 else ""))
        enum_out.append("\t}")
        enum_out.append("")
    if enum_out[-1] == "":
        enum_out.pop()
    enum_out.append("}")

    return "\n".join(out) + "\n", "\n".join(enum_out) + "\n"


def main() -> int:
    for framework, package in MODULES.items():
        api, enums = render(framework, package)
        target = pathlib.Path("src") / f"DatadogNet.{package}.iOS"
        (target / "ApiDefinitions.cs").write_text(api)
        has_enums = "public enum" in enums
        if has_enums:
            (target / "StructsAndEnums.cs").write_text(enums)
        else:
            (target / "StructsAndEnums.cs").unlink(missing_ok=True)
        print(f"{framework:<24} api={len(api.splitlines()):>5} lines  enums={'yes' if has_enums else 'no'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
