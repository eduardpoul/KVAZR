using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kvazr.Cli;

/// <summary>
/// Builds the device icons the application ships, for both of its stacks, out of the
/// drawings in <c>Design/kvazr/device-icons</c>: inline SVG for the web pane and
/// DrawingImage resources for the Avalonia shell.
///
/// The drawings are Fluent 2 colour icons — Microsoft's own where the library has one,
/// drawn to the same rules where it does not — and they are read through a deliberately
/// small piece of SVG: shapes filled flat or with a linear gradient in grid coordinates.
/// That is all the colour style needs, and all of it carries into Avalonia geometry
/// exactly. A drawing that steps outside it stops the build, because the alternative is
/// a rail that quietly draws something other than what the pane shows.
/// </summary>
internal static class FluentIconsBuild
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace Ui = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly HashSet<string> ShapeNames = ["path", "rect", "circle", "ellipse"];

    private static readonly HashSet<string> ShapeAttributes =
    [
        "d", "x", "y", "width", "height", "rx", "ry", "cx", "cy", "r",
        "fill", "fill-rule", "clip-rule", "opacity", "fill-opacity",
    ];

    private static readonly HashSet<string> GradientAttributes = ["id", "x1", "y1", "x2", "y2", "gradientUnits"];

    private static readonly HashSet<string> StopAttributes = ["offset", "stop-color", "stop-opacity"];

    public static int Run()
    {
        if (FindRoot() is not { } root)
        {
            Console.Error.WriteLine("Run it from inside the repository: there is no KVAZR.slnx above here.");
            return 1;
        }

        string source = Path.Combine(root, "Design", "kvazr", "device-icons");
        List<Icon> icons = [];

        foreach (string file in Directory.GetFiles(source, "*.svg").Order(StringComparer.Ordinal))
        {
            string key = Path.GetFileNameWithoutExtension(file);
            try
            {
                icons.Add(Read(key, XElement.Load(file)));
            }
            catch (Exception e) when (e is FormatException or XmlException)
            {
                Console.Error.WriteLine($"{key}.svg: {e.Message}");
                return 1;
            }
        }

        // The one every lookup falls back to, so a device nobody has drawn still gets a
        // picture rather than an empty tile.
        if (!icons.Exists(i => i.Key == "unknown"))
        {
            Console.Error.WriteLine("unknown.svg is missing: it is what a device with no drawing of its own shows.");
            return 1;
        }

        File.WriteAllText(Path.Combine(root, "src", "Kvazr.App", "Web", "Infrastructure", "FluentIcons.g.cs"),
            WebSource(icons), Utf8);
        File.WriteAllText(Path.Combine(root, "src", "Kvazr.App", "Themes", "FluentIcons.axaml"),
            ShellSource(icons), Utf8);

        Console.WriteLine($"{icons.Count} icons: {string.Join(", ", icons.Select(i => i.Key))}");
        Console.WriteLine("wrote src/Kvazr.App/Web/Infrastructure/FluentIcons.g.cs and src/Kvazr.App/Themes/FluentIcons.axaml");
        return 0;
    }

    // =====================================================================================
    // reading a drawing
    // =====================================================================================

    private static Icon Read(string key, XElement svg)
    {
        if (svg.Name != Svg + "svg") throw new FormatException("not an SVG document");
        if ((string?)svg.Attribute("viewBox") != "0 0 24 24")
            throw new FormatException("the icons are drawn on the Fluent 24 grid: viewBox=\"0 0 24 24\"");

        Dictionary<string, Gradient> gradients = new(StringComparer.Ordinal);
        List<XElement> shapes = [];

        foreach (XElement child in svg.Elements())
        {
            if (child.Name == Svg + "defs")
            {
                foreach (XElement definition in child.Elements())
                {
                    Gradient gradient = ReadGradient(definition);
                    if (!gradients.TryAdd(gradient.Id, gradient))
                        throw new FormatException($"two gradients called {gradient.Id}");
                }
            }
            else if (child.Name.Namespace == Svg && ShapeNames.Contains(child.Name.LocalName))
            {
                shapes.Add(child);
            }
            else
            {
                throw new FormatException($"<{child.Name.LocalName}> is outside what the shell can draw");
            }
        }

        foreach (XElement shape in shapes)
        {
            Only(shape, ShapeAttributes);
            foreach (XAttribute a in shape.Attributes())
            {
                if (a.Name.LocalName is not ("d" or "fill" or "fill-rule" or "clip-rule")) Number(a.Value, a.Name.LocalName);
            }

            string fill = Required(shape, "fill");
            if (Reference(fill) is { } id)
            {
                if (!gradients.ContainsKey(id)) throw new FormatException($"fill {fill} names no gradient");
            }
            else
            {
                Colour(fill);
            }
        }

        if (shapes.Count == 0) throw new FormatException("draws nothing");
        return new Icon(key, shapes, gradients);
    }

    private static Gradient ReadGradient(XElement element)
    {
        if (element.Name != Svg + "linearGradient")
            throw new FormatException($"<{element.Name.LocalName}>: only linear gradients carry over to Avalonia exactly");

        Only(element, GradientAttributes);

        // Grid coordinates, never the shape's bounding box: an Avalonia point without a
        // percentage is absolute, so the same four numbers mean the same line on both sides.
        if ((string?)element.Attribute("gradientUnits") != "userSpaceOnUse")
            throw new FormatException("gradients are drawn in grid coordinates: gradientUnits=\"userSpaceOnUse\"");

        List<Stop> stops = [];
        foreach (XElement stop in element.Elements())
        {
            if (stop.Name != Svg + "stop") throw new FormatException($"<{stop.Name.LocalName}> inside a gradient");
            Only(stop, StopAttributes);

            string offset = (string?)stop.Attribute("offset") ?? "0";
            double opacity = Number((string?)stop.Attribute("stop-opacity") ?? "1", "stop-opacity");
            Number(offset, "offset");

            int alpha = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
            stops.Add(new Stop(offset, $"#{alpha.ToString("X2", CultureInfo.InvariantCulture)}{Colour(Required(stop, "stop-color"))}"));
        }

        if (stops.Count == 0) throw new FormatException($"gradient {(string?)element.Attribute("id")} has no stops");

        string Point(string name)
        {
            string value = Required(element, name);
            Number(value, name);
            return value;
        }

        return new Gradient(element, Required(element, "id"),
            $"{Point("x1")},{Point("y1")}", $"{Point("x2")},{Point("y2")}", stops);
    }

    // =====================================================================================
    // the web pane: inline SVG
    // =====================================================================================

    private static string WebSource(List<Icon> icons)
    {
        StringBuilder text = new();
        text.AppendLine("// <auto-generated>");
        text.AppendLine("//   Из Design/kvazr/device-icons — kvazr --fluent-icons.");
        text.AppendLine("//   НЕ ПРАВИТЬ РУКАМИ: перерисовали иконку — перезапустить команду.");
        text.AppendLine("// </auto-generated>");
        text.AppendLine("#nullable enable");
        text.AppendLine();
        text.AppendLine("namespace Kvazr.App.Web;");
        text.AppendLine();
        text.AppendLine("/// <summary>The device icons, exactly as Design/kvazr/device-icons draws them.</summary>");
        text.AppendLine("public static class FluentIcons");
        text.AppendLine("{");
        text.AppendLine("    /// <summary>The icon for a key, or the unknown device's when the key is not in the set.</summary>");
        text.AppendLine("    public static string Svg(string key) => Icons.TryGetValue(key, out string? svg) ? svg : Icons[\"unknown\"];");
        text.AppendLine();
        text.AppendLine("    private static readonly Dictionary<string, string> Icons = new(System.StringComparer.Ordinal)");
        text.AppendLine("    {");

        foreach (Icon icon in icons)
        {
            text.AppendLine($"        [\"{icon.Key}\"] = @\"{WebSvg(icon).Replace("\"", "\"\"", StringComparison.Ordinal)}\",");
        }

        text.AppendLine("    };");
        text.AppendLine("}");
        return text.ToString();
    }

    /// <summary>
    /// The drawing as one inline element. Its gradient ids are renamed after the icon,
    /// because every icon on a page shares one document: two drawings that both call
    /// their gradient "a" would paint each other.
    /// </summary>
    private static string WebSvg(Icon icon)
    {
        XElement defs = new(Svg + "defs", icon.Gradients.Values.Select(g =>
        {
            XElement copy = new(g.Source);
            copy.SetAttributeValue("id", WebId(icon, g.Id));
            return copy;
        }));

        IEnumerable<XElement> shapes = icon.Shapes.Select(s =>
        {
            XElement copy = new(s);
            if (Reference((string)s.Attribute("fill")!) is { } id) copy.SetAttributeValue("fill", $"url(#{WebId(icon, id)})");
            return copy;
        });

        XElement svg = new(Svg + "svg",
            new XAttribute("width", "100%"), new XAttribute("height", "100%"),
            new XAttribute("viewBox", "0 0 24 24"), new XAttribute("fill", "none"),
            defs, shapes);

        return svg.ToString(SaveOptions.DisableFormatting);
    }

    private static string WebId(Icon icon, string id) => $"fi-{icon.Key}-{id}";

    // =====================================================================================
    // the Avalonia shell: DrawingImage resources keyed "Fi.<name>"
    // =====================================================================================

    private static string ShellSource(List<Icon> icons)
    {
        XElement dictionary = new(Ui + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", X));

        foreach (Icon icon in icons)
        {
            XElement group = new(Ui + "DrawingGroup",
                // the canvas, so every icon measures the whole grid whatever it draws on it
                new XElement(Ui + "GeometryDrawing", new XAttribute("Brush", "#00FFFFFF"),
                    new XElement(Ui + "GeometryDrawing.Geometry",
                        new XElement(Ui + "RectangleGeometry", new XAttribute("Rect", "0,0,24,24")))));

            foreach (XElement shape in icon.Shapes) group.Add(Drawing(icon, shape));

            dictionary.Add(new XElement(Ui + "DrawingImage", new XAttribute(X + "Key", $"Fi.{icon.Key}"), group));
        }

        XDocument document = new(
            // Not the command line itself: a double hyphen is not allowed inside an XML comment.
            new XComment("""

                  Из Design/kvazr/device-icons — команда kvazr с ключом fluent-icons.
                  НЕ ПРАВИТЬ РУКАМИ: перерисовали иконку — перезапустить команду.

                """),
            dictionary);

        StringBuilder text = new();
        XmlWriterSettings settings = new() { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true };
        using (XmlWriter writer = XmlWriter.Create(text, settings)) document.Save(writer);
        return text.AppendLine().ToString();
    }

    private static XElement Drawing(Icon icon, XElement shape)
    {
        string fill = (string)shape.Attribute("fill")!;

        XElement brush = Reference(fill) is { } id
            ? GradientBrush(icon.Gradients[id])
            : new XElement(Ui + "SolidColorBrush", new XAttribute("Color", $"#{Colour(fill)}"));

        // SVG keeps the shape's opacity and its fill's apart; a geometry drawing has only
        // the brush, and for a shape with nothing but a fill the product is the same thing.
        double opacity = Number((string?)shape.Attribute("opacity") ?? "1", "opacity")
                       * Number((string?)shape.Attribute("fill-opacity") ?? "1", "fill-opacity");
        if (opacity < 1) brush.SetAttributeValue("Opacity", opacity.ToString("0.###", CultureInfo.InvariantCulture));

        return new XElement(Ui + "GeometryDrawing",
            new XElement(Ui + "GeometryDrawing.Brush", brush),
            new XElement(Ui + "GeometryDrawing.Geometry", Geometry(shape)));
    }

    private static XElement GradientBrush(Gradient gradient) =>
        new(Ui + "LinearGradientBrush",
            new XAttribute("StartPoint", gradient.Start),
            new XAttribute("EndPoint", gradient.End),
            gradient.Stops.Select(s => new XElement(Ui + "GradientStop",
                new XAttribute("Offset", s.Offset), new XAttribute("Color", s.Colour))));

    /// <summary>
    /// The shape as Avalonia geometry. The fill rule is carried across rather than
    /// defaulted: Fluent draws a shape and the hole in it as two figures of one path, and
    /// under the non-zero rule the hole fills in.
    /// </summary>
    private static XElement Geometry(XElement shape)
    {
        string Value(string name, string fallback = "0") => (string?)shape.Attribute(name) ?? fallback;

        switch (shape.Name.LocalName)
        {
            case "path":
                XElement path = new(Ui + "PathGeometry", new XAttribute("Figures", Required(shape, "d")));
                if ((string?)shape.Attribute("fill-rule") == "evenodd") path.SetAttributeValue("FillRule", "EvenOdd");
                return path;

            case "rect":
                // One radius given stands for both, as in SVG.
                string rx = Value("rx", Value("ry")), ry = Value("ry", rx);
                return new XElement(Ui + "RectangleGeometry",
                    new XAttribute("Rect", $"{Value("x")},{Value("y")},{Required(shape, "width")},{Required(shape, "height")}"),
                    new XAttribute("RadiusX", rx), new XAttribute("RadiusY", ry));

            case "circle":
                return new XElement(Ui + "EllipseGeometry",
                    new XAttribute("Center", $"{Value("cx")},{Value("cy")}"),
                    new XAttribute("RadiusX", Required(shape, "r")), new XAttribute("RadiusY", Required(shape, "r")));

            default:
                return new XElement(Ui + "EllipseGeometry",
                    new XAttribute("Center", $"{Value("cx")},{Value("cy")}"),
                    new XAttribute("RadiusX", Required(shape, "rx")), new XAttribute("RadiusY", Required(shape, "ry")));
        }
    }

    // =====================================================================================
    // small parts
    // =====================================================================================

    private static string? FindRoot()
    {
        string[] starts = [Environment.CurrentDirectory, AppContext.BaseDirectory];

        foreach (string start in starts)
        {
            for (DirectoryInfo? at = new(start); at is not null; at = at.Parent)
            {
                if (File.Exists(Path.Combine(at.FullName, "KVAZR.slnx"))) return at.FullName;
            }
        }

        return null;
    }

    private static void Only(XElement element, HashSet<string> allowed)
    {
        foreach (XAttribute a in element.Attributes())
        {
            if (!allowed.Contains(a.Name.LocalName))
                throw new FormatException($"{a.Name.LocalName}=\"{a.Value}\" on <{element.Name.LocalName}> is outside what the shell can draw");
        }
    }

    private static string Required(XElement element, string name) =>
        (string?)element.Attribute(name) ?? throw new FormatException($"<{element.Name.LocalName}> has no {name}");

    private static double Number(string text, string what) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"{what}=\"{text}\" is not a plain number");

    private static string? Reference(string fill) =>
        fill.StartsWith("url(#", StringComparison.Ordinal) && fill.EndsWith(')') ? fill[5..^1] : null;

    /// <summary>A hex colour as RRGGBB, the short form widened.</summary>
    private static string Colour(string value)
    {
        string hex = value.StartsWith('#') ? value[1..] : throw new FormatException($"colour {value}: hex only");
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));

        return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? hex.ToUpperInvariant()
            : throw new FormatException($"colour {value} is not #rgb or #rrggbb");
    }

    private sealed record Icon(string Key, List<XElement> Shapes, Dictionary<string, Gradient> Gradients);

    private sealed record Gradient(XElement Source, string Id, string Start, string End, List<Stop> Stops);

    private sealed record Stop(string Offset, string Colour);
}
