namespace Domain.App.Models;

public sealed record EditorToolDescriptor(
    EditorMode Mode,
    string Identifier,
    string IconKey,
    string IconPathData,
    string Label,
    string Hint,
    string? ShortcutText,
    string CommandKey,
    string? InspectorPanelKey,
    string GroupKey,
    int Order,
    Editor3DTool? ThreeDTool = null,
    Editor2DTool? TwoDTool = null,
    EditorSidebarAction? Action = null,
    bool StartsSection = false,
    bool IsEnabled = true,
    bool IsSelected = false)
{
    public bool IsTool => ThreeDTool is not null || TwoDTool is not null;

    public bool IsAction => Action is not null;
}

public static class EditorToolCatalog
{
    public static IReadOnlyList<EditorToolDescriptor> All { get; } =
        CreateCatalog();

    public static IReadOnlyList<EditorToolDescriptor> ForMode(EditorMode mode)
        => All.Where(descriptor => descriptor.Mode == mode).ToArray();

    public static EditorToolDescriptor? Find(EditorMode mode, string commandKey)
        => All.FirstOrDefault(descriptor =>
            descriptor.Mode == mode
            && string.Equals(descriptor.CommandKey, commandKey, StringComparison.Ordinal));

    public static EditorToolDescriptor? FindByShortcut(EditorMode mode, string shortcutText)
        => FindByShortcut(All, mode, shortcutText);

    public static EditorToolDescriptor? FindByShortcut(
        IEnumerable<EditorToolDescriptor> descriptors,
        EditorMode mode,
        string shortcutText)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        if (string.IsNullOrWhiteSpace(shortcutText))
            return null;

        var normalizedShortcut = NormalizeShortcut(shortcutText);
        return descriptors.FirstOrDefault(descriptor =>
            descriptor.Mode == mode
            && descriptor.ShortcutText is not null
            && string.Equals(
                NormalizeShortcut(descriptor.ShortcutText),
                normalizedShortcut,
                StringComparison.Ordinal));
    }

    public static IReadOnlyList<EditorToolDescriptor> ApplyCustomizations(
        IEnumerable<EditorToolCustomization>? customizations)
    {
        if (customizations is null)
            return All;

        var customizationList = customizations.ToArray();
        var duplicateIdentifier = customizationList
            .GroupBy(customization => customization.Identifier, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicateIdentifier is not null)
        {
            throw new InvalidOperationException(
                $"Tool customization '{duplicateIdentifier.Key}' is defined more than once.");
        }

        var customizationByIdentifier = customizationList
            .Where(customization => !string.IsNullOrWhiteSpace(customization.Identifier))
            .ToDictionary(customization => customization.Identifier, StringComparer.Ordinal);
        var customized = All
            .Select(descriptor => customizationByIdentifier.TryGetValue(descriptor.Identifier, out var customization)
                ? descriptor with
                {
                    Order = customization.Order,
                    ShortcutText = NormalizeOptionalShortcut(customization.ShortcutText),
                }
                : descriptor)
            .ToArray();

        ValidateShortcutUniqueness(customized);
        return customized
            .GroupBy(descriptor => descriptor.Mode)
            .SelectMany(modeGroup =>
            {
                var ordered = modeGroup
                    .OrderBy(descriptor => descriptor.Order)
                    .ThenBy(descriptor => descriptor.Identifier, StringComparer.Ordinal)
                    .ToArray();
                return ordered.Select((descriptor, index) => descriptor with
                {
                    StartsSection = index > 0
                        && !string.Equals(
                            ordered[index - 1].GroupKey,
                            descriptor.GroupKey,
                            StringComparison.Ordinal),
                });
            })
            .ToArray();
    }

    public static void ValidateShortcutUniqueness(IEnumerable<EditorToolDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var duplicate = descriptors
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.ShortcutText))
            .GroupBy(
                descriptor => (descriptor.Mode, Shortcut: NormalizeShortcut(descriptor.ShortcutText!)))
            .FirstOrDefault(group => group.Skip(1).Any());

        if (duplicate is null)
            return;

        var identifiers = string.Join(", ", duplicate.Select(descriptor => descriptor.Identifier));
        throw new InvalidOperationException(
            $"Shortcut '{duplicate.Key.Shortcut}' is assigned more than once in {duplicate.Key.Mode} mode: {identifiers}.");
    }

    private static IReadOnlyList<EditorToolDescriptor> CreateCatalog()
    {
        var descriptors = CreateThreeDDescriptors()
            .Concat(CreateTwoDDescriptors())
            .ToArray();
        ValidateShortcutUniqueness(descriptors);
        return descriptors;
    }

    private static string NormalizeShortcut(string shortcutText)
        => shortcutText.Trim().ToUpperInvariant();

    private static string? NormalizeOptionalShortcut(string? shortcutText)
        => string.IsNullOrWhiteSpace(shortcutText) ? null : NormalizeShortcut(shortcutText);

    private static IReadOnlyList<EditorToolDescriptor> CreateThreeDDescriptors()
        => AssignOrder(
    [
        // Fluent UI System Icons (regular, 24px): Cursor, Arrow Move, Grid, Ruler, Arrow Export, Document Arrow Right.
        ThreeD(
            "select",
            "Select",
            "Select and inspect faces in the 3D workspace.",
            "M5.5 3.48325C5.5 2.23498 6.93571 1.53286 7.92098 2.29927L21.4353 12.8116C22.5626 13.6885 21.9425 15.4956 20.5143 15.4956H13.6619C13.1574 15.4956 12.6806 15.7264 12.3676 16.1222L8.17661 21.4224C7.2945 22.538 5.5 21.9142 5.5 20.492L5.5 3.48325ZM20.5143 13.9956L7 3.48325L7 20.492L11.191 15.1918C11.7884 14.4363 12.6987 13.9956 13.6619 13.9956H20.5143Z",
            ShortcutText: "1",
            Tool: Editor3DTool.Select),
        ThreeD(
            "move",
            "Move",
            "Translate whole bodies with the viewport gizmo or exact offsets.",
            "M15.2803 6.03033C14.9874 6.32322 14.5126 6.32322 14.2197 6.03033L12.75 4.56066L12.75 8.25C12.75 8.66421 12.4142 9 12 9C11.5858 9 11.25 8.66421 11.25 8.25L11.25 4.56066L9.78033 6.03033C9.48744 6.32322 9.01256 6.32322 8.71967 6.03033C8.42678 5.73744 8.42678 5.26256 8.71967 4.96967L11.4697 2.21967C11.6103 2.07902 11.8011 2 12 2C12.1989 2 12.3897 2.07902 12.5303 2.21967L15.2803 4.96967C15.5732 5.26256 15.5732 5.73744 15.2803 6.03033ZM6.03033 14.2197C6.32322 14.5126 6.32322 14.9874 6.03033 15.2803C5.73744 15.5732 5.26256 15.5732 4.96967 15.2803L2.21967 12.5303C2.07902 12.3897 2 12.1989 2 12C2 11.8011 2.07902 11.6103 2.21967 11.4697L4.96967 8.71967C5.26256 8.42678 5.73744 8.42678 6.03033 8.71967C6.32322 9.01256 6.32322 9.48744 6.03033 9.78033L4.56066 11.25H8.25C8.66421 11.25 9 11.5858 9 12C9 12.4142 8.66421 12.75 8.25 12.75H4.56066L6.03033 14.2197ZM17.9697 15.2803C17.6768 14.9874 17.6768 14.5126 17.9697 14.2197L19.4393 12.75H15.75C15.3358 12.75 15 12.4142 15 12C15 11.5858 15.3358 11.25 15.75 11.25H19.4393L17.9697 9.78033C17.6768 9.48744 17.6768 9.01256 17.9697 8.71967C18.2626 8.42678 18.7374 8.42678 19.0303 8.71967L21.7803 11.4697C21.921 11.6103 22 11.8011 22 12C22 12.1989 21.921 12.3897 21.7803 12.5303L19.0303 15.2803C18.7374 15.5732 18.2626 15.5732 17.9697 15.2803ZM15.2803 17.9697C14.9874 17.6768 14.5126 17.6768 14.2197 17.9697L12.75 19.4393L12.75 15.75C12.75 15.3358 12.4142 15 12 15C11.5858 15 11.25 15.3358 11.25 15.75L11.25 19.4393L9.78033 17.9697C9.48744 17.6768 9.01256 17.6768 8.71967 17.9697C8.42678 18.2626 8.42678 18.7374 8.71967 19.0303L11.4697 21.7803C11.6103 21.921 11.8011 22 12 22C12.1989 22 12.3897 21.921 12.5303 21.7803L15.2803 19.0303C15.5732 18.7374 15.5732 18.2626 15.2803 17.9697Z",
            ShortcutText: "2",
            Tool: Editor3DTool.Move),
        ThreeD(
            "project",
            "Project",
            "Create a projection sketch from an origin plane or model face.",
            "M8.75 13C9.99264 13 11 14.0074 11 15.25V18.75C11 19.9926 9.99264 21 8.75 21H5.25C4.00736 21 3 19.9926 3 18.75V15.25C3 14.0074 4.00736 13 5.25 13H8.75ZM18.75 13C19.9926 13 21 14.0074 21 15.25V18.75C21 19.9926 19.9926 21 18.75 21H15.25C14.0074 21 13 19.9926 13 18.75V15.25C13 14.0074 14.0074 13 15.25 13H18.75ZM8.75 14.5H5.25C4.83579 14.5 4.5 14.8358 4.5 15.25V18.75C4.5 19.1642 4.83579 19.5 5.25 19.5H8.75C9.16421 19.5 9.5 19.1642 9.5 18.75V15.25C9.5 14.8358 9.16421 14.5 8.75 14.5ZM18.75 14.5H15.25C14.8358 14.5 14.5 14.8358 14.5 15.25V18.75C14.5 19.1642 14.8358 19.5 15.25 19.5H18.75C19.1642 19.5 19.5 19.1642 19.5 18.75V15.25C19.5 14.8358 19.1642 14.5 18.75 14.5ZM8.75 3C9.99264 3 11 4.00736 11 5.25V8.75C11 9.99264 9.99264 11 8.75 11H5.25C4.00736 11 3 9.99264 3 8.75V5.25C3 4.00736 4.00736 3 5.25 3H8.75ZM18.75 3C19.9926 3 21 4.00736 21 5.25V8.75C21 9.99264 19.9926 11 18.75 11H15.25C14.0074 11 13 9.99264 13 8.75V5.25C13 4.00736 14.0074 3 15.25 3H18.75ZM8.75 4.5H5.25C4.83579 4.5 4.5 4.83579 4.5 5.25V8.75C4.5 9.16421 4.83579 9.5 5.25 9.5H8.75C9.16421 9.5 9.5 9.16421 9.5 8.75V5.25C9.5 4.83579 9.16421 4.5 8.75 4.5ZM18.75 4.5H15.25C14.8358 4.5 14.5 4.83579 14.5 5.25V8.75C14.5 9.16421 14.8358 9.5 15.25 9.5H18.75C19.1642 9.5 19.5 9.16421 19.5 8.75V5.25C19.5 4.83579 19.1642 4.5 18.75 4.5Z",
            ShortcutText: "3",
            Tool: Editor3DTool.Plane),
        ThreeD(
            "measure",
            "Measure",
            "Inspect a single face and review flattening distortion.",
            "M9.25 2C8.00736 2 7 3.00736 7 4.25V19.75C7 20.9926 8.00736 22 9.25 22H14.75C15.9926 22 17 20.9926 17 19.75V4.25C17 3.00736 15.9926 2 14.75 2H9.25ZM8.5 19H10.25C10.6642 19 11 18.6642 11 18.25C11 17.8358 10.6642 17.5 10.25 17.5H8.5V16H12.25C12.6642 16 13 15.6642 13 15.25C13 14.8358 12.6642 14.5 12.25 14.5H8.5V12.75H10.25C10.6642 12.75 11 12.4142 11 12C11 11.5858 10.6642 11.25 10.25 11.25H8.5V9.5H12.25C12.6642 9.5 13 9.16421 13 8.75C13 8.33579 12.6642 8 12.25 8H8.5V6.5H10.25C10.6642 6.5 11 6.16421 11 5.75C11 5.33579 10.6642 5 10.25 5H8.5V4.25C8.5 3.83579 8.83579 3.5 9.25 3.5H14.75C15.1642 3.5 15.5 3.83579 15.5 4.25V19.75C15.5 20.1642 15.1642 20.5 14.75 20.5H9.25C8.83579 20.5 8.5 20.1642 8.5 19.75V19Z",
            ShortcutText: "4",
            Tool: Editor3DTool.Measure,
            StartsSection: true),
        ThreeD(
            "unfold",
            "Unfold",
            "Flatten the selected faces or body and preview the generated 2D output.",
            "M2.7525 4.5C3.1322 4.5 3.44594 4.78227 3.49554 5.14836L3.50237 5.25014L3.5 18.2542C3.49992 18.6684 3.16408 19.0041 2.74986 19.0041C2.37017 19.0041 2.05642 18.7218 2.00683 18.3557L2 18.2539L2.00237 5.24986C2.00244 4.83565 2.33829 4.5 2.7525 4.5ZM15.6471 6.30373L15.7197 6.21961C15.986 5.95338 16.4027 5.92921 16.6963 6.1471L16.7804 6.21972L21.777 11.2174C22.0431 11.4835 22.0674 11.8999 21.8498 12.1935L21.7773 12.2776L16.7807 17.2811C16.488 17.5742 16.0131 17.5745 15.72 17.2818C15.4536 17.0157 15.4291 16.5991 15.6467 16.3053L15.7193 16.2211L19.43 12.504L5.75295 12.5049C5.37326 12.5049 5.05946 12.2228 5.0098 11.8567L5.00295 11.7549C5.00295 11.3752 5.28511 11.0615 5.65118 11.0118L5.75295 11.0049L19.443 11.004L15.7196 7.28028C15.4534 7.01398 15.4292 6.59732 15.6471 6.30373L15.7197 6.21961L15.6471 6.30373Z",
            ShortcutText: "5",
            Tool: Editor3DTool.Unfold,
            StartsSection: true),
        ThreeD(
            "output",
            "Output",
            "Review the generated DXF path, preview it in-app, or open the exported file.",
            "M18.5 20A.5.5 0 0 1 18 20.5H12.27A6.52 6.52 0 0 1 11.19 22H18A2 2 0 0 0 20 20V9.83A2 2 0 0 0 19.41 8.41L13.6 2.6A2 2 0 0 0 12.18 2H6A2 2 0 0 0 4 4V11.5C4.47 11.3 4.98 11.16 5.5 11.08V4C5.5 3.72 5.72 3.5 6 3.5H12V8C12 9.1 12.9 10 14 10H18.5V20ZM13.5 4.62 17.38 8.5H14A.5.5 0 0 1 13.5 8V4.62ZM12 17.5A5.5 5.5 0 1 1 1 17.5 5.5 5.5 0 0 1 12 17.5ZM3.5 17A.5.5 0 0 0 3.5 18H8.29L6.65 19.65A.5.5 0 0 0 7.35 20.35L9.85 17.85A.5.5 0 0 0 9.85 17.15L7.35 14.65A.5.5 0 0 0 6.65 15.35L8.29 17H3.5Z",
            ShortcutText: "6",
            Tool: Editor3DTool.Output),
        ThreeD(
            "frame-home",
            "Frame",
            "Recenter and fit the visible 3D workspace.",
            "M12 3.69 4.5 9.52V19a1.5 1.5 0 0 0 1.5 1.5h4.25v-5.25h3.5v5.25H18A1.5 1.5 0 0 0 19.5 19V9.52L12 3.69ZM3.58 8.82l7.5-5.83a1.5 1.5 0 0 1 1.84 0l7.5 5.83a.75.75 0 1 1-.92 1.18L19 9.83V19a3 3 0 0 1-3 3h-3a.75.75 0 0 1-.75-.75V16h-2v5.25A.75.75 0 0 1 9.5 22h-3a3 3 0 0 1-3-3V10L4.5 10a.75.75 0 1 1-.92-1.18Z",
            ShortcutText: "H",
            Action: EditorSidebarAction.FrameHome,
            StartsSection: true),
        ThreeD(
            "camera-mode",
            "Camera",
            "Toggle perspective and orthographic camera.",
            "M7.75 4A2.75 2.75 0 0 0 5 6.75v8.5A2.75 2.75 0 0 0 7.75 18H9v1.25A2.75 2.75 0 0 0 11.75 22h4.5A2.75 2.75 0 0 0 19 19.25v-8.5A2.75 2.75 0 0 0 16.25 8H15V6.75A2.75 2.75 0 0 0 12.25 4h-4.5ZM6.5 6.75c0-.69.56-1.25 1.25-1.25h4.5c.69 0 1.25.56 1.25 1.25V8h-1.75A2.75 2.75 0 0 0 9 10.75v5.75H7.75c-.69 0-1.25-.56-1.25-1.25v-8.5Zm4 4c0-.69.56-1.25 1.25-1.25h4.5c.69 0 1.25.56 1.25 1.25v8.5c0 .69-.56 1.25-1.25 1.25h-4.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z",
            ShortcutText: "O",
            Action: EditorSidebarAction.ToggleOrthographic),
    ]);

    private static IReadOnlyList<EditorToolDescriptor> CreateTwoDDescriptors()
        => AssignOrder(
        [
            TwoD(Editor2DTool.Select, "select", "Select", "Select and inspect 2D entities.", "1", "cursor", "2d.selection", "navigation"),
            TwoD(Editor2DTool.Move, "move", "Move", "Move the current 2D selection.", "2", "move", "2d.transform", "navigation"),
            TwoD(Editor2DTool.Pan, "pan", "Pan", "Pan the 2D canvas without editing geometry.", "3", "pan", "2d.navigation", "navigation"),
            TwoD(Editor2DTool.Measure, "measure", "Measure", "Create reference measurements in the 2D document.", "4", "ruler", "2d.measure", "precision", StartsSection: true),
            TwoD(Editor2DTool.Dimension, "dimension", "Dimension", "Create attached or reference dimensions.", "D", "dimension", "2d.dimension", "precision"),
            TwoD(Editor2DTool.Scale, "scale", "Scale", "Scale selected 2D geometry.", "S", "scale", "2d.transform", "modify", StartsSection: true),
            TwoD(Editor2DTool.Mirror, "mirror", "Mirror", "Mirror selected geometry across a picked axis.", "M", "mirror", "2d.mirror", "modify"),
            TwoDAction(EditorSidebarAction.FlipSelectionHorizontal, "flip-horizontal", "Flip Horizontal", "Flip selected geometry horizontally in place.", "Ctrl+Shift+H", "flip-horizontal", "2d.transform", "modify"),
            TwoDAction(EditorSidebarAction.FlipSelectionVertical, "flip-vertical", "Flip Vertical", "Flip selected geometry vertically in place.", "Ctrl+Shift+J", "flip-vertical", "2d.transform", "modify"),
            TwoDAction(EditorSidebarAction.DuplicateSelection, "duplicate", "Duplicate", "Duplicate selected geometry with a small offset.", "Ctrl+D", "duplicate", "2d.transform", "modify"),
            TwoD(Editor2DTool.Offset, "offset", "Offset", "Create offset copies of selected geometry.", "O", "offset", "2d.offset", "modify"),
            TwoD(Editor2DTool.AddThickness, "add-thickness", "Thickness", "Thicken open centerlines into closed outlines.", null, "thickness", "2d.thickness", "modify"),
            TwoD(Editor2DTool.Cleanup, "cleanup", "Cleanup", "Join nearby endpoints and remove degenerate segments.", "J", "cleanup", "2d.cleanup", "modify"),
            TwoD(Editor2DTool.Patterning, "pattern", "Pattern", "Create rectangular or circular copies.", null, "pattern", "2d.pattern", "modify"),
            TwoD(Editor2DTool.PaperFolding, "paper-folding", "Paper Folding", "Create crease geometry and glue tabs.", null, "fold", "2d.paper-folding", "modify"),
            TwoD(Editor2DTool.AddSewingHoles, "add-sewing-holes", "Add Holes / Sewing", "Add configurable sewing holes along selected paths.", null, "sewing", "2d.sewing", "modify"),
            TwoD(Editor2DTool.Trim, "trim", "Trim", "Trim linework between intersections.", "X", "trim", "2d.trim", "modify"),
            TwoD(Editor2DTool.Fillet, "fillet", "Fillet", "Round selected 2D corners.", "F", "fillet", "2d.corner", "modify"),
            TwoD(Editor2DTool.Chamfer, "chamfer", "Chamfer", "Bevel selected 2D corners.", "B", "chamfer", "2d.corner", "modify"),
            TwoD(Editor2DTool.ConvertLines, "convert-lines", "Convert Lines", "Replace linework with a patterned style.", "E", "convert-lines", "2d.convert-lines", "modify"),
            TwoD(Editor2DTool.SketchLine, "line", "Line", "Create straight line segments.", "L", "line", "2d.line", "create", StartsSection: true),
            TwoD(Editor2DTool.SketchRectangle, "rectangle", "Rectangle", "Create constrained rectangles.", "R", "rectangle", "2d.rectangle", "create"),
            TwoD(Editor2DTool.SketchCircle, "circle", "Circle", "Create circles from a center and radius.", "C", "circle", "2d.circle", "create"),
            TwoD(Editor2DTool.SketchText, "text", "Text", "Create editable text entities.", "T", "text", "2d.text", "create"),
            TwoD(Editor2DTool.Pen, "pen", "Pen", "Create open or closed polyline paths.", "P", "pen", "2d.pen", "create"),
            TwoD(Editor2DTool.SketchPolygon, "polygon", "Polygon", "Create regular polygons.", null, "polygon", "2d.polygon", "create"),
        ]);

    private static IReadOnlyList<EditorToolDescriptor> AssignOrder(IReadOnlyList<EditorToolDescriptor> descriptors)
        => descriptors
            .Select((descriptor, index) => descriptor with
            {
                Order = index,
                StartsSection = index > 0
                    && !string.Equals(descriptors[index - 1].GroupKey, descriptor.GroupKey, StringComparison.Ordinal),
            })
            .ToArray();

    private static EditorToolDescriptor ThreeD(
        string CommandKey,
        string Label,
        string Hint,
        string IconPathData,
        string? ShortcutText = null,
        Editor3DTool? Tool = null,
        EditorSidebarAction? Action = null,
        bool StartsSection = false)
        => new(
            EditorMode.ThreeD,
            $"3d.{CommandKey}",
            CommandKey,
            IconPathData,
            Label,
            Hint,
            ShortcutText,
            CommandKey,
            GetThreeDInspectorPanelKey(CommandKey),
            GetThreeDGroupKey(CommandKey),
            Order: 0,
            ThreeDTool: Tool,
            Action: Action,
            StartsSection: StartsSection);

    private static EditorToolDescriptor TwoD(
        Editor2DTool Tool,
        string CommandKey,
        string Label,
        string Hint,
        string? ShortcutText,
        string IconKey,
        string InspectorPanelKey,
        string GroupKey,
        bool StartsSection = false)
        => new(
            EditorMode.TwoD,
            $"2d.{CommandKey}",
            IconKey,
            IconPathData: EditorTwoDToolIconCatalog.GetPathData(IconKey),
            Label,
            Hint,
            ShortcutText,
            CommandKey,
            InspectorPanelKey,
            GroupKey,
            Order: 0,
            TwoDTool: Tool,
            StartsSection: StartsSection);

    private static EditorToolDescriptor TwoDAction(
        EditorSidebarAction Action,
        string CommandKey,
        string Label,
        string Hint,
        string? ShortcutText,
        string IconKey,
        string InspectorPanelKey,
        string GroupKey)
        => new(
            EditorMode.TwoD,
            $"2d.{CommandKey}",
            IconKey,
            IconPathData: EditorTwoDToolIconCatalog.GetPathData(IconKey),
            Label,
            Hint,
            ShortcutText,
            CommandKey,
            InspectorPanelKey,
            GroupKey,
            Order: 0,
            Action: Action);

    private static string? GetThreeDInspectorPanelKey(string commandKey)
        => commandKey switch
        {
            "select" => "3d.selection",
            "move" => "3d.move-bodies",
            "project" => "3d.projection",
            "measure" => "3d.measure",
            "unfold" => "3d.unfold",
            "output" => "3d.output",
            _ => null,
        };

    private static string GetThreeDGroupKey(string commandKey)
        => commandKey switch
        {
            "select" or "move" or "project" => "navigation",
            "measure" => "inspect",
            "unfold" or "output" => "output",
            _ => "view",
        };
}

public static class EditorCommandPaletteCatalog
{
    public const string ToggleGridIdentifier = "view.grid";
    public const string ToggleSnappingIdentifier = "view.snap";
    public const string ToggleChainSelectionIdentifier = "view.chainSelect";
    public const string ZoomInIdentifier = "view.zoomIn";
    public const string ZoomOutIdentifier = "view.zoomOut";
    public const string ZoomToFitIdentifier = "view.zoomFit";
    public const string ToggleActivityLogIdentifier = "view.toggleLogs";
    public const string UndoIdentifier = "edit.undo";
    public const string RedoIdentifier = "edit.redo";
    public const string DeleteIdentifier = "edit.delete";
    public const string SwitchToTwoDIdentifier = "view.mode2D";
    public const string SwitchToThreeDIdentifier = "view.mode3D";
    public const string SwitchToBatchIdentifier = "view.modeBatch";
    public const string NewIdentifier = "file.new";
    public const string OpenIdentifier = "file.open";
    public const string ImportIdentifier = "file.import";
    public const string SaveIdentifier = "file.save";
    public const string SaveAsIdentifier = "file.saveAs";
    public const string ExportDxfIdentifier = "file.export.dxf";
    public const string ExportSvgIdentifier = "file.export.svg";
    public const string ExportPngIdentifier = "file.export.png";
    public const string ExportPdfIdentifier = "file.export.pdf";
    public const string StartScreenIdentifier = "file.startScreen";
    public const string ClearReferenceImageIdentifier = "image.clearRef";
    public const string SearchIdentifier = "app.search";
    public const string PreferencesIdentifier = "app.preferences";
    public const string DocumentationIdentifier = "app.documentation";

    public static IReadOnlyList<EditorToolDescriptor> SearchOnly { get; } =
        CreateSearchOnly();

    private static IReadOnlyList<EditorToolDescriptor> CreateSearchOnly()
    {
        var commands = new List<EditorToolDescriptor>();
        foreach (var mode in Enum.GetValues<EditorMode>())
        {
            commands.AddRange(
            [
                Command(mode, ToggleGridIdentifier, "Toggle Grid", "View · Show or hide the 2D grid.", "Shift+G", "view"),
                Command(mode, ToggleSnappingIdentifier, "Toggle Snapping", "View · Enable or disable 2D snapping.", "N", "view"),
                Command(mode, ToggleChainSelectionIdentifier, "Toggle Chain Selection", "View · Enable or disable connected-path selection.", "A", "view"),
                Command(mode, ZoomInIdentifier, "Zoom In", "View · Increase the 2D viewport zoom.", "Ctrl+=", "view"),
                Command(mode, ZoomOutIdentifier, "Zoom Out", "View · Decrease the 2D viewport zoom.", "Ctrl+-", "view"),
                Command(mode, ZoomToFitIdentifier, "Zoom to Fit", "View · Frame all visible 2D content.", null, "view"),
                Command(mode, ToggleActivityLogIdentifier, "Toggle Log Tray", "View · Show or hide the persisted activity log.", null, "view"),
                Command(mode, UndoIdentifier, "Undo", "Edit · Undo the last workspace change.", "Ctrl+Z", "edit"),
                Command(mode, RedoIdentifier, "Redo", "Edit · Redo the last workspace change.", "Ctrl+Shift+Z", "edit"),
                Command(mode, DeleteIdentifier, "Delete Selection", "Edit · Delete selected geometry or measurement.", "Delete", "edit"),
                Command(mode, SwitchToTwoDIdentifier, "Switch to 2D Mode", "View · Show the 2D workspace.", null, "view"),
                Command(mode, SwitchToThreeDIdentifier, "Switch to 3D Mode", "View · Show the 3D workspace.", null, "view"),
                Command(mode, SwitchToBatchIdentifier, "Switch to Batch Mode", "View · Show the batch workspace.", null, "view"),
                Command(mode, NewIdentifier, "New Project", "File · Create a new project.", "Ctrl+N", "file"),
                Command(mode, OpenIdentifier, "Open Project…", "File · Open an existing Pathstitch project.", "Ctrl+O", "file"),
                Command(mode, ImportIdentifier, "Import…", "File · Import drawing, image, or 3D files.", "Ctrl+Shift+I", "file"),
                Command(mode, SaveIdentifier, "Save Project", "File · Save changes to the current project.", "Ctrl+S", "file"),
                Command(mode, SaveAsIdentifier, "Save Project As…", "File · Save the current project to another path.", "Ctrl+Shift+S", "file"),
                Command(mode, ExportDxfIdentifier, "Export DXF…", "File · Export the 2D workspace as DXF.", "Ctrl+E", "file"),
                Command(mode, ExportSvgIdentifier, "Export SVG…", "File · Export the 2D workspace as SVG.", "Ctrl+Shift+E", "file"),
                Command(mode, ExportPngIdentifier, "Export PNG…", "File · Export the 2D workspace as PNG.", null, "file"),
                Command(mode, ExportPdfIdentifier, "Export PDF…", "File · Export the 2D workspace as PDF.", null, "file"),
                Command(mode, StartScreenIdentifier, "Start Screen", "File · Show the Pathstitch start screen.", null, "file"),
                Command(mode, ClearReferenceImageIdentifier, "Clear Active Reference Image", "File · Remove the active reference-image layer.", null, "file"),
                Command(mode, SearchIdentifier, "Search Commands…", "App · Search tools and commands.", "Ctrl+K", "app"),
                Command(mode, PreferencesIdentifier, "Preferences…", "App · Open Pathstitch preferences.", null, "app"),
                Command(mode, DocumentationIdentifier, "Documentation", "App · Open Pathstitch documentation.", null, "app"),
            ]);
        }
        return commands;
    }

    private static EditorToolDescriptor Command(
        EditorMode mode,
        string identifier,
        string label,
        string hint,
        string? shortcutText,
        string groupKey)
        => new(
            mode,
            identifier,
            IconKey: identifier,
            IconPathData: string.Empty,
            label,
            hint,
            shortcutText,
            CommandKey: identifier,
            InspectorPanelKey: null,
            groupKey,
            Order: 0);
}
