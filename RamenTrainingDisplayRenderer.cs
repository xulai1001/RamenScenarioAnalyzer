using System.Collections.Immutable;
using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TAttribute = Terminal.Gui.Drawing.Attribute;
using TColor = Terminal.Gui.Drawing.Color;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace RamenScenarioAnalyzer;

internal sealed record RamenDisplaySnapshot(
    int MainWidth,
    ImmutableArray<RamenPanelSnapshot> Headers,
    ImmutableArray<RamenDisplayLine> ImportantRows,
    ImmutableArray<RamenPanelSnapshot> ScenarioPanels,
    ImmutableArray<RamenTrainingCardSnapshot> TrainingCards,
    ImmutableArray<RamenExtraSectionSnapshot> ExtraSections)
{
    public static RamenDisplaySnapshot Create(RamenTrainingDisplayBuilder builder)
    {
        var mainWidth = CommandInfoLayout.Current.MainSectionWidth;
        var extraSections = new List<RamenExtraSectionSnapshot>();
        if (builder.ExtraRows.Count != 0)
            extraSections.Add(RamenExtraSectionSnapshot.Create("Ramen", builder.ExtraRows));
        extraSections.AddRange(builder.ExtraSections
            .Where(section => section.Rows.Count != 0)
            .Select(RamenExtraSectionSnapshot.Create));
        return new(
            mainWidth,
            [.. builder.HeaderPanels.Select(RamenPanelSnapshot.Create)],
            [.. builder.ImportantRows.Lines.Select(Copy)],
            [.. builder.ScenarioPanels.Select(RamenPanelSnapshot.Create)],
            [.. builder.TrainingCards.Select(RamenTrainingCardSnapshot.Create)],
            [.. extraSections]);
    }

    internal static RamenDisplayLine Copy(RamenDisplayLine line)
        => new(line.Segments.ToImmutableArray(), line.IsRule);
}

internal sealed record RamenExtraSectionSnapshot(string Title, ImmutableArray<RamenDisplayLine> Rows)
{
    public static RamenExtraSectionSnapshot Create(RamenExtraSection section)
        => Create(section.Title, section.Rows);

    public static RamenExtraSectionSnapshot Create(string title, RamenDisplayRows rows)
        => new(title, [.. rows.Lines.Select(RamenDisplaySnapshot.Copy)]);
}

internal sealed record RamenPanelSnapshot(
    string Key,
    string Title,
    bool ShowHeader,
    ImmutableArray<RamenDisplayLine> Lines)
{
    public static RamenPanelSnapshot Create(RamenDisplayPanel panel)
        => new(
            panel.Key,
            panel.Title,
            panel.ShowHeader,
            [.. panel.Lines.Select(RamenDisplaySnapshot.Copy)]);
}

internal sealed record RamenTrainingCardSnapshot(
    int TrainIndex,
    RamenDisplayLine Title,
    bool Highlighted,
    ImmutableArray<RamenDisplayLine> Rows)
{
    public static RamenTrainingCardSnapshot Create(RamenTrainingCard card)
        => new(
            card.TrainIndex,
            RamenDisplaySnapshot.Copy(card.StyledTitle),
            card.Highlighted,
            [.. card.Rows.Lines.Select(RamenDisplaySnapshot.Copy)]);
}

internal static class RamenTrainingDisplayRenderer
{
    public static WorkspaceContent Render(RamenDisplaySnapshot snapshot)
        => new(() => new RamenDashboardView(snapshot));

    internal static bool TryScroll(View view, Command command)
    {
        if (view is not RamenDashboardView dashboard)
            return false;
        return dashboard.Scroll(command);
    }

    sealed class RamenDashboardView : View
    {
        const int HeaderHeight = 3;
        const int ImportantHeight = 5;
        const int ScenarioHeight = 3;
        const int MinimumTrainingHeight = 16;
        static readonly int[] HeaderRatios = [4, 6, 6, 3];

        readonly int mainWidth;
        readonly int minimumContentWidth;
        readonly int topAreaHeight;
        readonly View main;
        readonly FrameView extras;
        readonly FrameView aiPanel;
        readonly ImmutableArray<RamenDisplayLine> extraRows;
        readonly ImmutableArray<RamenDisplayLine> aiRows;

        public RamenDashboardView(RamenDisplaySnapshot snapshot)
        {
            Id = "ramen-root";
            Width = Dim.Fill();
            Height = Dim.Fill();
            CanFocus = true;
            TabStop = TabBehavior.TabGroup;
            ViewportSettings = ViewportSettingsFlags.HasScrollBars;
            AddCommand(Command.PageUp, () => Scroll(Command.PageUp));
            AddCommand(Command.PageDown, () => Scroll(Command.PageDown));
            AddCommand(Command.Start, () => Scroll(Command.Start));
            AddCommand(Command.End, () => Scroll(Command.End));
            KeyBindings.ReplaceCommands(Key.PageUp, Command.PageUp);
            KeyBindings.ReplaceCommands(Key.PageDown, Command.PageDown);
            KeyBindings.ReplaceCommands(Key.Home, Command.Start);
            KeyBindings.ReplaceCommands(Key.End, Command.End);

            mainWidth = snapshot.MainWidth;
            minimumContentWidth = mainWidth + (mainWidth + 3) / 4;
            // 让出 3 行给下半区 AI 面板
            var trainingHeight = Math.Max(MinimumTrainingHeight, snapshot.TrainingCards.Length == 0
                    ? 0
                    : snapshot.TrainingCards.Max(x => x.Rows.Length));
            topAreaHeight = HeaderHeight + ImportantHeight + ScenarioHeight + trainingHeight;
            var normal = GetAttributeForRole(VisualRole.Normal);
            var palette = new RamenPalette(normal);
            SetScheme(palette.BaseScheme);
            // 仅命名 "AI" 的分区下沉到下半区 AI 面板，其余 Extra 分区保留右侧竖列
            var otherSections = new List<RamenExtraSectionSnapshot>();
            var aiSections = new List<RamenExtraSectionSnapshot>();
            foreach (var section in snapshot.ExtraSections)
            {
                if (section.Title == "AI")
                    aiSections.Add(section);
                else
                    otherSections.Add(section);
            }

            extraRows = [.. otherSections.SelectMany(section =>
                section.Rows.Prepend(RamenDisplayLine.Colored(section.Title, RamenDisplayColor.Cyan)))];
            aiRows = [.. aiSections.SelectMany(section =>
                section.Rows.Prepend(RamenDisplayLine.Colored(section.Title, RamenDisplayColor.Cyan)))];

            main = new View
            {
                Id = "ramen-main",
                X = 0,
                Y = 0,
                Width = mainWidth,
                Height = Dim.Fill()
            };
            main.SetScheme(palette.BaseScheme);
            AddHeaderFrames(snapshot.Headers, palette);
            main.Add(CreateFrame(
                "ramen-important",
                null,
                0,
                HeaderHeight,
                mainWidth,
                ImportantHeight,
                snapshot.ImportantRows,
                palette,
                wordWrap: true));
            AddScenarioFrames(snapshot.ScenarioPanels, palette);
            AddTrainingFrames(snapshot.TrainingCards, palette, trainingHeight);

            extras = CreateFrame(
                "ramen-extras",
                null,
                mainWidth,
                0,
                minimumContentWidth - mainWidth,
                Dim.Fill(),
                extraRows,
                palette,
                wordWrap: true);
            aiPanel = CreateFrame(
                "ramen-ai",
                null,
                0,
                topAreaHeight,
                mainWidth,
                Dim.Fill(),
                aiRows,
                palette,
                wordWrap: true);
            Add(main, extras, aiPanel);
        }

        protected override void OnSubViewLayout(LayoutEventArgs args)
        {
            var frameWidth = Math.Max(1, Frame.Width);
            var frameHeight = Math.Max(1, Frame.Height);
            var contentWidth = Math.Max(frameWidth, minimumContentWidth);
            var visibleHeight = Math.Max(1, frameHeight - (contentWidth > frameWidth ? 1 : 0));
            var rightTextWidth = Math.Max(1, contentWidth - mainWidth - 4);
            var rightExtraHeight = extraRows.Length == 0
                ? 0
                : extraRows.Sum(row => row.Text.Length == 0
                    ? 1
                    : TextFormatter.WordWrapText(row.Text, rightTextWidth).Count()) + 2;
            // AI 面板与 Ramen 主面板同宽（mainWidth），上下排布在左侧。
            var aiTextWidth = Math.Max(1, mainWidth - 4);
            var aiHeight = aiRows.Length == 0
                ? 0
                : aiRows.Sum(row => row.Text.Length == 0
                    ? 1
                    : TextFormatter.WordWrapText(row.Text, aiTextWidth).Count()) + 2;
            var contentHeight = Math.Max(visibleHeight, Math.Max(topAreaHeight + aiHeight, rightExtraHeight));
            var contentSize = new Size(contentWidth, contentHeight);
            if (GetContentSize() != contentSize)
                SetContentSize(contentSize);

            main.Height = topAreaHeight;
            // Extra 面板在右侧，占据全部高度。
            extras.Width = contentWidth - mainWidth;
            extras.Height = contentHeight;
            aiPanel.Y = topAreaHeight;
            aiPanel.Width = mainWidth;
            aiPanel.Height = aiHeight;

            var maxX = Math.Max(0, contentWidth - Math.Max(1, Viewport.Width));
            var maxY = Math.Max(0, contentHeight - Math.Max(1, Viewport.Height));
            if (Viewport.X > maxX || Viewport.Y > maxY)
            {
                Viewport = new Rectangle(
                    Math.Min(Viewport.X, maxX),
                    Math.Min(Viewport.Y, maxY),
                    Viewport.Width,
                    Viewport.Height);
            }

            base.OnSubViewLayout(args);
        }

        internal bool Scroll(Command command)
        {
            var amount = command switch
            {
                Command.PageUp => -Math.Max(1, Viewport.Height),
                Command.PageDown => Math.Max(1, Viewport.Height),
                Command.Start => -GetContentSize().Height,
                Command.End => GetContentSize().Height,
                _ => 0,
            };
            if (amount == 0)
                return false;

            ScrollVertical(amount);
            return true;
        }

        void AddHeaderFrames(IReadOnlyList<RamenPanelSnapshot> panels, RamenPalette palette)
        {
            var x = 0;
            var ratioTotal = HeaderRatios.Sum();
            var ratioUsed = 0;
            for (var i = 0; i < panels.Count; i++)
            {
                ratioUsed += HeaderRatios[i];
                var nextX = i == panels.Count - 1 ? mainWidth : mainWidth * ratioUsed / ratioTotal;
                var width = nextX - x;
                main.Add(CreateFrame(
                    $"ramen-header:{panels[i].Key}",
                    null,
                    x,
                    0,
                    width,
                    HeaderHeight,
                    panels[i].Lines,
                    palette,
                    wordWrap: false));
                x = nextX;
            }
        }

        void AddScenarioFrames(IReadOnlyList<RamenPanelSnapshot> panels, RamenPalette palette)
        {
            if (panels.Count == 0)
                return;

            var x = 0;
            for (var i = 0; i < panels.Count; i++)
            {
                var nextX = i == panels.Count - 1 ? mainWidth : mainWidth * (i + 1) / panels.Count;
                var width = nextX - x;
                main.Add(CreateFrame(
                    $"ramen-scenario:{panels[i].Key}",
                    panels[i].ShowHeader ? panels[i].Title : null,
                    x,
                    HeaderHeight + ImportantHeight,
                    width,
                    ScenarioHeight,
                    panels[i].Lines,
                    palette,
                    wordWrap: false));
                x = nextX;
            }
        }

        void AddTrainingFrames(
            IReadOnlyList<RamenTrainingCardSnapshot> cards,
            RamenPalette palette,
            int trainingHeight)
        {
            if (cards.Count == 0)
            {
                main.Add(CreateTextView(
                    [RamenDisplayLine.Plain("无训练信息")],
                    palette,
                    wordWrap: false,
                    x: 0,
                    y: HeaderHeight + ImportantHeight + ScenarioHeight,
                    width: mainWidth,
                    height: 1));
                return;
            }

            var cardWidth = mainWidth / 5;
            var innerWidth = cardWidth - 4;
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var frame = new FrameView
                {
                    Id = $"ramen-training:{card.TrainIndex}",
                    X = i * cardWidth,
                    Y = HeaderHeight + ImportantHeight + ScenarioHeight,
                    Width = cardWidth,
                    Height = trainingHeight
                };
                frame.SetScheme(palette.BaseScheme);
                if (card.Highlighted)
                {
                    frame.Border.GetOrCreateView().SetScheme(
                        new Scheme(palette.Attribute(RamenDisplayColor.LightGreen)));
                }

                var y = 0;
                var titleWidth = Math.Min(innerWidth, card.Title.Text.GetColumns());
                frame.Add(CreateTextView(
                    [card.Title],
                    palette,
                    wordWrap: false,
                    x: Pos.Align(Alignment.Center),
                    y: y++,
                    width: titleWidth,
                    height: 1));
                frame.Add(new Line { X = 1, Y = y++, Width = innerWidth });
                foreach (var row in card.Rows)
                {
                    if (row.IsRule)
                    {
                        frame.Add(new Line { X = 1, Y = y++, Width = innerWidth });
                        continue;
                    }

                    frame.Add(CreateTextView(
                        [row],
                        palette,
                        wordWrap: false,
                        x: 1,
                        y: y++,
                        width: innerWidth,
                        height: 1));
                }
                main.Add(frame);
            }
        }

        static FrameView CreateFrame(
            string id,
            string? title,
            Pos x,
            Pos y,
            Dim width,
            Dim height,
            IReadOnlyList<RamenDisplayLine> rows,
            RamenPalette palette,
            bool wordWrap)
        {
            var frame = new FrameView
            {
                Id = id,
                Title = title ?? string.Empty,
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
            frame.SetScheme(palette.BaseScheme);
            frame.Add(CreateTextView(
                rows,
                palette,
                wordWrap,
                x: 1,
                y: 0,
                width: Dim.Fill(1),
                height: Dim.Fill()));
            return frame;
        }

        static TextView CreateTextView(
            IReadOnlyList<RamenDisplayLine> rows,
            RamenPalette palette,
            bool wordWrap,
            Pos x,
            Pos y,
            Dim width,
            Dim height)
        {
            var text = new StyledTextView(palette)
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                ReadOnly = true,
                CanFocus = false,
                WordWrap = wordWrap
            };
            text.Load([.. rows.Select(row => ToCells(row, palette))]);
            return text;
        }

        sealed class StyledTextView : TextView
        {
            readonly TAttribute blackNormal;

            public StyledTextView(RamenPalette palette)
            {
                blackNormal = palette.Normal;
                SetScheme(palette.BaseScheme);
            }

            protected override void OnDrawNormalColor(List<Cell> line, int idxCol, int idxRow)
                => SetAttribute(line[idxCol].Attribute ?? blackNormal);

            protected override void OnDrawReadOnlyColor(List<Cell> line, int idxCol, int idxRow)
                => SetAttribute(line[idxCol].Attribute ?? blackNormal);

            protected override void OnDrawUsedColor(List<Cell> line, int idxCol, int idxRow)
                => SetAttribute(line[idxCol].Attribute ?? blackNormal);
        }

        static List<Cell> ToCells(RamenDisplayLine line, RamenPalette palette)
        {
            var cells = new List<Cell>();
            foreach (var segment in line.Segments)
                cells.AddRange(Cell.ToCellList(segment.Text, palette.Attribute(segment.Color)));
            return cells;
        }

    }

    sealed class RamenPalette
    {
        readonly TAttribute normal;

        public RamenPalette(TAttribute normal)
        {
            this.normal = normal;
            Normal = new(normal.Foreground, TColor.Black, normal.Style);
            BaseScheme = new Scheme
            {
                Normal = Normal,
                ReadOnly = Normal,
                Focus = Normal
            };
        }

        public TAttribute Normal { get; }
        public Scheme BaseScheme { get; }

        public TAttribute Attribute(RamenDisplayColor color)
            => color is RamenDisplayColor.Normal
                ? Normal
                : new TAttribute(Foreground(color), TColor.Black, normal.Style);

        TColor Foreground(RamenDisplayColor color) => color switch
        {
            RamenDisplayColor.Cyan => new(StandardColor.BrightCyan),
            RamenDisplayColor.Green => new(StandardColor.Green),
            RamenDisplayColor.Yellow => new(StandardColor.BrightYellow),
            RamenDisplayColor.Red => new(StandardColor.BrightRed),
            RamenDisplayColor.DarkOrange => new(StandardColor.DarkOrange),
            RamenDisplayColor.Aqua => new(StandardColor.BrightCyan),
            RamenDisplayColor.Lime => new(StandardColor.BrightGreen),
            RamenDisplayColor.LightGreen => new(StandardColor.LightGreen),
            _ => normal.Foreground
        };
    }
}
