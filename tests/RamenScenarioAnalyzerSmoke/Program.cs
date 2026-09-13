using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Xml.Linq;
using Gallop;
using Gallop.Endpoints;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RamenScenarioAnalyzer;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Text;
using Terminal.Gui.Time;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using TColor = Terminal.Gui.Drawing.Color;
using UraConfig = UmamusumeResponseAnalyzer.Config;
using UraDatabase = UmamusumeResponseAnalyzer.Database;
using RamenPlugin = RamenScenarioAnalyzer.RamenScenarioAnalyzer;

Environment.SetEnvironmentVariable("DisableRealDriverIO", "1");
var previousUiCulture = Thread.CurrentThread.CurrentUICulture;
var phaseOrdinal = 0;
Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
try
{
    RunPhase(nameof(TestTrainingRowsAreScrollable), TestTrainingRowsAreScrollable);
    RunPhase(nameof(TestDefaultDisplayShapeAndRender), TestDefaultDisplayShapeAndRender);
    RunPhase(nameof(TestHorizontalScrollingAndResize), TestHorizontalScrollingAndResize);
    RunPhase(nameof(TestTerminalGuiLayoutAndColors), TestTerminalGuiLayoutAndColors);
    RunPhase(nameof(TestExtraSections), TestExtraSections);
    RunPhase(nameof(TestTrainingPartnerColorSemantics), TestTrainingPartnerColorSemantics);
    RunPhase(nameof(TestDefaultDisplayAllowsMissingCommandFeelingReward), TestDefaultDisplayAllowsMissingCommandFeelingReward);
    RunPhase(nameof(TestTrainingPartnerNamesUseNameManager), TestTrainingPartnerNamesUseNameManager);
    RunPhase(nameof(TestFriendSupportCardDoesNotShine), TestFriendSupportCardDoesNotShine);
    RunPhase(nameof(TestGroupSupportCardShinesOnMatchingTraining), TestGroupSupportCardShinesOnMatchingTraining);
    RunPhase(nameof(TestSupportCardShinesOnlyOnMatchingTrainingType), TestSupportCardShinesOnlyOnMatchingTrainingType);
    RunPhase(nameof(TestFriendshipTrainingSupportCardNameIsPlainText), TestFriendshipTrainingSupportCardNameIsPlainText);
    RunPhase(nameof(TestFriendSupportCardNameIsPlainText), TestFriendSupportCardNameIsPlainText);
    RunPhase(nameof(TestModifierSurface), TestModifierSurface);
    RunPhase(nameof(TestProjectMetadataAndDependency), TestProjectMetadataAndDependency);
    using var ui = new WorkspaceSmokeSession();
    RunPhase(nameof(TestAnalyzerRegistrations), () => TestAnalyzerRegistrations(ui.Application));
    await RunPhaseAsync(nameof(TestWorkspaceAndFullBleed), () => TestWorkspaceAndFullBleed(ui));
    await RunPhaseAsync(nameof(TestRegisteredModifierLifecycle), () => TestRegisteredModifierLifecycle(ui));
    await RunPhaseAsync(nameof(TestKeyedHistoryAndInput), () => TestKeyedHistoryAndInput(ui));
    await RunPhaseAsync(nameof(TestLoadAnalyzerRendersTrainingPanel), () => TestLoadAnalyzerRendersTrainingPanel(ui));
}
finally
{
    Thread.CurrentThread.CurrentUICulture = previousUiCulture;
}

void RunPhase(string name, Action action)
{
    var marker = $"[Ramen smoke phase {++phaseOrdinal:D2}] {name}";
    Console.Error.WriteLine($"{marker}: start");
    action();
    Console.Error.WriteLine($"{marker}: complete");
}

async ValueTask RunPhaseAsync(string name, Func<ValueTask> action)
{
    var marker = $"[Ramen smoke phase {++phaseOrdinal:D2}] {name}";
    Console.Error.WriteLine($"{marker}: start");
    await action();
    Console.Error.WriteLine($"{marker}: complete");
}

static void TestDefaultDisplayShapeAndRender()
{
    var context = CreateDisplayContext(includeDuplicatedScenarioCommands: true);
    var builder = RamenTrainingDisplayBuilder.CreateDefault(context);

    RequireCount(builder.TrainingCards.Count, 5, "Default training card count");
    RequireSequence([.. builder.TrainingCards.Select(x => x.TrainIndex)], [1, 2, 3, 4, 5], "Default training card order");

    foreach (var card in builder.TrainingCards)
    {
        if (card.Rows.Any(row => row.Contains("训练次数", StringComparison.Ordinal)))
            throw new InvalidOperationException("Ramen-specific scenario data leaked into the default training card flow.");
    }

    var speedRows = RenderRows(Require(builder.FindTrainingCardByCommandId(101), "Speed card").Rows);
    if (!speedRows.Contains("Lv5 | 12") || speedRows.Contains("Lv5 | 7"))
        throw new InvalidOperationException("Speed card did not render its own command feeling beside train level.");

    var staminaRows = RenderRows(Require(builder.FindTrainingCardByCommandId(105), "Stamina card").Rows);
    if (!staminaRows.Contains("Lv4 | 8") || staminaRows.Contains("Lv4 | 5"))
        throw new InvalidOperationException("Stamina card did not render its own command feeling beside train level.");

    var powerRows = RenderRows(Require(builder.FindTrainingCardByCommandId(102), "Power card").Rows);
    if (!powerRows.Contains("Lv3 | 9"))
        throw new InvalidOperationException("Power card did not render its own command feeling beside train level.");

    var gutsRows = RenderRows(Require(builder.FindTrainingCardByCommandId(103), "Guts card").Rows);
    if (!gutsRows.Contains("Lv2 | 9"))
        throw new InvalidOperationException("Guts card did not render its own command feeling beside train level.");

    var wizRows = RenderRows(Require(builder.FindTrainingCardByCommandId(106), "Wiz card").Rows);
    if (!wizRows.Contains("Lv1 | 11"))
        throw new InvalidOperationException("Wiz card did not render its own command feeling beside train level.");

    if (builder.ScenarioPanels.Count < 4)
        throw new InvalidOperationException("Ramen scenario panels were not created.");
    if (builder.ExtraRows.Count < 2)
        throw new InvalidOperationException("Ramen extra rows were not created.");

    var rendered = Render(RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(builder)));
    foreach (var expected in new[] { "速度", "耐力", "力量", "根性", "智力", "URAF", "Lv5 | 12" })
    {
        if (!rendered.Contains(expected))
            throw new InvalidOperationException($"Rendered training panel does not contain '{expected}'.");
    }

    if (rendered.Contains("心得剩余:"))
        throw new InvalidOperationException("Command feeling rewards should not be rendered in the extra area.");
    if (rendered.Contains("命令心得:"))
        throw new InvalidOperationException("Command feelings should not be rendered in the extra area.");
}

static void TestTrainingRowsAreScrollable()
{
    var context = CreateDisplayContext(includeDuplicatedScenarioCommands: false);
    var builder = RamenTrainingDisplayBuilder.CreateDefault(context);
    var speed = Require(builder.FindTrainingCardByTrainIndex(TrainIndex.Speed), "Speed card");
    for (var i = 1; i <= 12; i++)
        speed.AddRow($"extended row {i:00}");
    speed.AddRow("extended tail");

    var captures = CaptureVerticalScrollSequence(
        RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(builder)));
    RequireEqual(true, captures.Initial.VerticalScrollBarVisible, "Extended training vertical scrollbar");
    if (captures.Initial.RootContentSize.Height <= captures.Initial.RootViewport.Height)
        throw new InvalidOperationException("Extended training cards must increase the dashboard content height.");
    if (!captures.Initial.Text.Contains("extended row 01", StringComparison.Ordinal))
        throw new InvalidOperationException("An appended training row must render in the training card.");
    if (captures.Initial.Text.Contains("extended tail", StringComparison.Ordinal))
        throw new InvalidOperationException("The last appended training row should start below the initial viewport.");
    if (captures.Scrolled.VerticalScrollBarValue <= 0 ||
        !captures.Scrolled.Text.Contains("extended tail", StringComparison.Ordinal))
        throw new InvalidOperationException("Vertical scrolling must make an appended training row reachable.");

}

static void TestDefaultDisplayAllowsMissingCommandFeelingReward()
{
    var context = CreateDisplayContext(
        includeDuplicatedScenarioCommands: false,
        missingCommandFeelingRewardId: 101);
    var builder = RamenTrainingDisplayBuilder.CreateDefault(context);
    var speedRows = RenderRows(Require(builder.FindTrainingCardByCommandId(101), "Speed card").Rows);

    if (!speedRows.Contains("Lv5") || speedRows.Contains("Lv5 |"))
        throw new InvalidOperationException("Missing command feeling reward should render the train level without a reward separator.");
}

static void TestTerminalGuiLayoutAndColors()
{
    var builder = RamenTrainingDisplayBuilder.CreateDefault(
        CreateDisplayContext(includeDuplicatedScenarioCommands: true));
    var capture = CaptureDashboard(
        RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(builder)));

    const string dateText = "1年 1月后半";
    var dateToken = FindCellToken(capture, dateText);
    RequireBlackBackground(
        capture,
        new(dateToken.Points[0].X + dateText.GetColumns(), dateToken.Points[0].Y),
        "Date header text tail");
    RequireEqual("─", capture.Cells[13, 2].Grapheme, "Training-card rule glyph");

    if (!capture.Text.Contains("训练次数: 101 = 3", StringComparison.Ordinal))
        throw new InvalidOperationException("Ramen extras must render in the right-side column.");
    if (FindCellToken(capture, "训练次数: 101 = 3").Points[0].X < 95)
        throw new InvalidOperationException("Ramen extras are not visible in the final right-side framebuffer.");

    var titles = new[] { "速度", "耐力", "力量", "根性(20%)", "智力" };
    var trainingColumns = new[] { 0, 19, 38, 57, 76 };
    for (var i = 0; i < titles.Length; i++)
    {
        var title = titles[i];
        var token = FindCellToken(capture, title);
        var titleWidth = title.GetColumns();
        var expectedStart = trainingColumns[i] + (19 - titleWidth) / 2;
        RequireEqual(new Point(expectedStart, 12), token.Points[0], $"{title} title origin");
        RequireEqual(token.Points[0].X + 2, token.Points[1].X, $"{title} CJK cell width");
        RequireEqual(token.Points[0].Y, token.Points[1].Y, $"{title} CJK row");
        RequireBlackBackground(capture, new(expectedStart - 1, 12), $"{title} title left blank");
        RequireBlackBackground(
            capture,
            new(expectedStart + titleWidth, 12),
            $"{title} title right blank");
    }

    foreach (var removed in new[] { "== 训练信息 ==", "▶", "★" })
    {
        if (capture.Text.Contains(removed, StringComparison.Ordinal))
            throw new InvalidOperationException($"Terminal.Gui output must not contain migration-only marker '{removed}'.");
    }

    RequireForeground(capture, "总属性: 600, Pt: 100", 0, new(StandardColor.BrightCyan), "Total");
    RequireForeground(capture, "80/100", 0, new(StandardColor.Green), "Vital current value");
    RequireForeground(capture, "绝好调", 0, new(StandardColor.Green), "Best motivation");
    RequireForeground(capture, "(20%)", 0, new(StandardColor.DarkOrange), "20 percent failure rate");
    RequireForeground(capture, "属:19|Pt:13", 2, new(StandardColor.BrightCyan), "Maximum stat gain");
}

static void TestExtraSections()
{
    foreach (var invalid in new[] { string.Empty, " ", "two\nlines", "two\rlines" })
    {
        try
        {
            using var _ = RamenTrainingDisplay.RegisterPartProducer(invalid);
            throw new InvalidOperationException("Ramen accepted an empty or multiline Extra source title.");
        }
        catch (ArgumentException)
        {
        }
    }

    var singleBuilder = new RamenTrainingDisplayBuilder();
    var singleRows = new RamenDisplayRows();
    singleRows.Add(RamenDisplayLine.Colored("solo-body", RamenDisplayColor.Yellow));
    singleBuilder.ExtraSections.Add(new("Solo", singleRows));
    var singleContent = RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(singleBuilder));
    singleRows.Add("late section mutation");
    var single = CaptureDashboard(singleContent);
    RequireForeground(single, "Solo", 0, new(StandardColor.BrightCyan), "Single section title");
    RequireForeground(single, "solo-body", 0, new(StandardColor.BrightYellow), "Single section body");
    var soloTitle = FindCellToken(single, "Solo").Points[0];
    var soloBody = FindCellToken(single, "solo-body").Points[0];
    RequireEqual(soloTitle.X, soloBody.X, "Single section body indentation");
    RequireEqual(soloTitle.Y + 1, soloBody.Y, "Single section row continuity");
    if (single.Text.Contains("late section mutation", StringComparison.Ordinal))
        throw new InvalidOperationException("Ramen snapshot retained mutable producer Extra rows.");

    const string wrapped = "123456789012345678901ABCDEFGHIJKLMNO";
    const string eventWrapped = "ABCDEFGHIJKLMNO123456789012345678901";
    const int wideExtraTextWidth = 21;
    var builder = new RamenTrainingDisplayBuilder();
    builder.ExtraRows.Add(RamenDisplayLine.Colored(wrapped, RamenDisplayColor.Green));
    builder.ExtraSections.Add(new("Empty", new RamenDisplayRows()));
    var eventRows = new RamenDisplayRows();
    eventRows.Add(RamenDisplayLine.Colored(eventWrapped, RamenDisplayColor.Yellow));
    builder.ExtraSections.Add(new("EventLogger", eventRows));
    var aiRows = new RamenDisplayRows();
    aiRows.Add("ai-body");
    builder.ExtraSections.Add(new("AI", aiRows));
    var grouped = CaptureDashboard(
        RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(builder)),
        height: 100);
    var ramenTitle = FindCellToken(grouped, "Ramen").Points[0];
    var ramenBody = FindCellToken(grouped, wrapped[..wideExtraTextWidth]).Points[0];
    var eventTitle = FindCellToken(grouped, "EventLogger").Points[0];
    var eventBody = FindCellToken(grouped, eventWrapped[..wideExtraTextWidth]).Points[0];
    var aiTitle = FindCellToken(grouped, "AI").Points[0];
    var aiBody = FindCellToken(grouped, "ai-body").Points[0];
    var wrappedHeight = TextFormatter.WordWrapText(wrapped, wideExtraTextWidth).Count();
    RequireEqual(ramenTitle.X, ramenBody.X, "Ramen body indentation");
    RequireEqual(ramenBody.Y + wrappedHeight, eventTitle.Y, "EventLogger title after wrapped Ramen body");
    RequireEqual(eventTitle.X, eventBody.X, "EventLogger body indentation");
    RequireEqual(aiTitle.X, aiBody.X, "AI panel body indentation");
    if (aiTitle.X >= ramenTitle.X)
        throw new InvalidOperationException("AI section must render on the left side below the main panel, not in the right Extra column.");
    if (aiTitle.Y <= eventBody.Y)
        throw new InvalidOperationException("AI section must render below the Ramen main panel.");
    RequireForeground(grouped, "Ramen", 0, new(StandardColor.BrightCyan), "Ramen section title");
    RequireForeground(grouped, "EventLogger", 0, new(StandardColor.BrightCyan), "EventLogger section title");
    RequireForeground(grouped, "AI", 0, new(StandardColor.BrightCyan), "AI section title");
    RequireForeground(grouped, wrapped[..wideExtraTextWidth], 0, new(StandardColor.Green), "Ramen section body");
    RequireForeground(grouped, eventWrapped[..wideExtraTextWidth], 0, new(StandardColor.BrightYellow), "EventLogger section body");
    if (grouped.Text.Contains("Empty", StringComparison.Ordinal))
        throw new InvalidOperationException("Ramen rendered an empty Extra section.");

    var resizeBuilder = new RamenTrainingDisplayBuilder();
    var resizeRows = new RamenDisplayRows();
    foreach (var index in Enumerable.Range(1, 17))
        resizeRows.Add($"{index:00}abcdefghijklmnopqrs");
    resizeBuilder.ExtraSections.Add(new("EventLogger", resizeRows));
    var resizeTailRows = new RamenDisplayRows();
    resizeTailRows.Add("ramen-resize-tail");
    resizeBuilder.ExtraSections.Add(new("AI", resizeTailRows));
    var resized = CaptureResizeSequence(RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(resizeBuilder)));
    if (resized.Narrow.RootContentSize.Height <= resized.Wide.RootContentSize.Height)
    {
        throw new InvalidOperationException(
            $"Narrow Ramen Extra width did not increase wrapped content height: wide content={resized.Wide.RootContentSize}, viewport={resized.Wide.RootViewport}; narrow content={resized.Narrow.RootContentSize}, viewport={resized.Narrow.RootViewport}.");
    }
    RequireEqual(resized.Wide.RootContentSize, resized.Restored.RootContentSize, "Restored Ramen Extra content size");
    RequireEqual(0, resized.Restored.RootViewport.X, "Restored Ramen Extra viewport X");
    RequireEqual(resized.Wide.Text, resized.Restored.Text, "Restored Ramen Extra framebuffer");

    var overflowBuilder = new RamenTrainingDisplayBuilder();
    var overflowRows = new RamenDisplayRows();
    foreach (var index in Enumerable.Range(1, 34))
        overflowRows.Add($"中{index:00}abcdefghijklmnopq");
    overflowBuilder.ExtraSections.Add(new("EventLogger", overflowRows));
    var tailRows = new RamenDisplayRows();
    tailRows.Add("ramen-extra-tail");
    overflowBuilder.ExtraSections.Add(new("AI", tailRows));
    var overflowContent = RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(overflowBuilder));
    var scrolled = CaptureVerticalScrollSequence(overflowContent);
    RequireEqual(true, scrolled.Initial.VerticalScrollBarVisible, "Overflow Ramen Extra vertical scrollbar");
    if (scrolled.PageDown.VerticalScrollBarValue <= 0)
        throw new InvalidOperationException("PageDown did not scroll the unified Ramen dashboard.");
    if (!scrolled.Scrolled.Text.Contains("AI", StringComparison.Ordinal)
        || !scrolled.Scrolled.Text.Contains("ramen-extra-tail", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("End did not reach the final Ramen Extra section.");
    }
}

static void TestTrainingPartnerColorSemantics()
{
    EnsureHostConfigInitialized();
    using var names = new SmokeDatabaseScope(CreateTrainingPartnerNames(supportType: 101));

    {
        var friendshipBuilder = RamenTrainingDisplayBuilder.CreateDefault(CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true));
        var friendship = CaptureDashboard(RamenTrainingDisplayRenderer.Render(
            RamenDisplaySnapshot.Create(friendshipBuilder)));
        RequireForeground(friendship, "[速]ライス80", 0, new(StandardColor.BrightCyan), "Friendship-training support name");
        RequireForeground(friendship, "[速]ライス80", 6, new(StandardColor.BrightRed), "Friendship value below 100");
        RequireBorderForeground(friendship, new(0, 11, 19, 19), new(StandardColor.LightGreen), "Shining training border");

        names.Publish(CreateTrainingPartnerNames(supportType: 0, supportNickname: "涼花"));
        var friendBuilder = RamenTrainingDisplayBuilder.CreateDefault(CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true));
        var friend = CaptureDashboard(RamenTrainingDisplayRenderer.Render(
            RamenDisplaySnapshot.Create(friendBuilder)));
        RequireForeground(friend, "[友]涼花80", 0, new(StandardColor.BrightGreen), "Friend support name");
        RequireForeground(friend, "[友]涼花80", 5, new(StandardColor.BrightRed), "Friend support value below 100");
        if (BorderForeground(friend, new(0, 11, 19, 19)) == new TColor(StandardColor.LightGreen))
            throw new InvalidOperationException("A non-shining friend support card must not highlight the training border.");
    }
}

static void TestHorizontalScrollingAndResize()
{
    var builder = RamenTrainingDisplayBuilder.CreateDefault(
        CreateDisplayContext(includeDuplicatedScenarioCommands: false));
    var captures = CaptureResizeSequence(RamenTrainingDisplayRenderer.Render(
        RamenDisplaySnapshot.Create(builder)));

    RequireEqual(new Rectangle(0, 0, 120, 36), captures.Wide.RootFrame, "Wide root frame");
    RequireEqual(false, captures.Wide.HorizontalScrollBarVisible, "Wide horizontal scrollbar");
    RequireEqual(new Size(120, 36), captures.Wide.RootContentSize, "Wide content size");

    RequireEqual(new Rectangle(0, 0, 80, 30), captures.Narrow.RootFrame, "Narrow root frame");
    RequireEqual(true, captures.Narrow.HorizontalScrollBarVisible, "Narrow horizontal scrollbar");
    RequireEqual(new Size(119, 30), captures.Narrow.RootContentSize, "Narrow content size");
    RequireEqual(true, captures.Narrow.VerticalScrollBarVisible, "Narrow vertical scrollbar");
    if (captures.Narrow.Text.Contains("训练次数: 101 = 3", StringComparison.Ordinal))
        throw new InvalidOperationException("Extras should start outside the initial narrow viewport.");

    if (captures.NarrowScrolled.HorizontalScrollBarValue <= 0 ||
        !captures.NarrowScrolled.Text.Contains("训练次数: 101 = 3", StringComparison.Ordinal))
        throw new InvalidOperationException("Horizontal scrolling must make the right-side Extras column reachable.");

    RequireEqual(new Rectangle(0, 0, 120, 36), captures.Restored.RootFrame, "Restored root frame");
    RequireEqual(false, captures.Restored.HorizontalScrollBarVisible, "Restored horizontal scrollbar");
    RequireEqual(0, captures.Restored.RootViewport.X, "Restored viewport X");
    RequireEqual(new Size(120, 36), captures.Restored.RootContentSize, "Restored content size");
    RequireEqual(captures.Wide.Text, captures.Restored.Text, "Restored framebuffer");
}

static void TestTrainingPartnerNamesUseNameManager()
{
    EnsureHostConfigInitialized();
    using var names = new SmokeDatabaseScope([
        new BaseName(101, "理事長", "理事"),
        new BaseName(1001, "ライスシャワー", "ライス"),
        new SupportCardName(30001, "幸せは曲がり角の向こう", "ライス", 101, 1001)
    ]);

    {
        var context = CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true);
        var builder = RamenTrainingDisplayBuilder.CreateDefault(context);
        var rendered = Render(RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(builder)));

        if (!rendered.Contains("ライス") || !rendered.Contains("理事"))
            throw new InvalidOperationException("Training partner names did not use NameManager.");
        if (rendered.Contains("支援30001") || rendered.Contains("角色101"))
            throw new InvalidOperationException("Training partner names still expose raw ids.");
    }
}

static void TestFriendSupportCardDoesNotShine()
{
    EnsureHostConfigInitialized();
    using var names = new SmokeDatabaseScope([
        new BaseName(101, "理事長", "理事"),
        new BaseName(1001, "ライスシャワー", "ライス"),
        new SupportCardName(30001, "幸せは曲がり角の向こう", "ライス", 0, 1001)
    ]);

    {
        var context = CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true);
        var builder = RamenTrainingDisplayBuilder.CreateDefault(context);
        var speed = Require(builder.FindTrainingCardByTrainIndex(TrainIndex.Speed), "Speed card");

        if (speed.Highlighted)
            throw new InvalidOperationException("Friend support card should not mark friendship training.");
    }
}

static void TestGroupSupportCardShinesOnMatchingTraining()
{
    EnsureHostConfigInitialized();
    using var names = new SmokeDatabaseScope(CreateTrainingPartnerNames(
        supportType: 0,
        supportNickname: "老登",
        supportCardId: 30241,
        charaId: 9047,
        charaName: "老登",
        charaNickname: "老登"));

    {
        var context = CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true,
            supportCardId: 30241,
            trainingPartnerCommandId: 102,
            friendship: 90);
        var builder = RamenTrainingDisplayBuilder.CreateDefault(context);
        var power = Require(builder.FindTrainingCardByTrainIndex(TrainIndex.Power), "Power card");

        if (!power.Highlighted)
            throw new InvalidOperationException("Legend group support card should mark friendship training on Power.");

        var groupPartner = FindTrainingPartner(context, TrainIndex.Power, "老登");
        RequireEqual("[友]老登90", groupPartner.Name, "Group support card name");
    }
}

static void TestSupportCardShinesOnlyOnMatchingTrainingType()
{
    EnsureHostConfigInitialized();
    using var names = new SmokeDatabaseScope(CreateTrainingPartnerNames(supportType: 106));

    {
        var wrongTypeBuilder = RamenTrainingDisplayBuilder.CreateDefault(CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true));
        var wrongTypeSpeed = Require(wrongTypeBuilder.FindTrainingCardByTrainIndex(TrainIndex.Speed), "Speed card");

        if (wrongTypeSpeed.Highlighted)
            throw new InvalidOperationException("Support card should not mark friendship training outside its own training type.");

        names.Publish(CreateTrainingPartnerNames(supportType: 101));
        var matchingTypeBuilder = RamenTrainingDisplayBuilder.CreateDefault(CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true));
        var matchingTypeSpeed = Require(matchingTypeBuilder.FindTrainingCardByTrainIndex(TrainIndex.Speed), "Speed card");

        if (!matchingTypeSpeed.Highlighted)
            throw new InvalidOperationException("Support card should mark friendship training on its own training type.");
    }
}

static void TestFriendshipTrainingSupportCardNameIsPlainText()
{
    EnsureHostConfigInitialized();
    using var names = new SmokeDatabaseScope(CreateTrainingPartnerNames(supportType: 101));

    {
        var matchingTypeContext = CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true);
        var matchingTypePartner = FindTrainingPartner(matchingTypeContext, TrainIndex.Speed, "ライス");

        RequireEqual("[速]ライス80", matchingTypePartner.Name, "Matching support card name");

        names.Publish(CreateTrainingPartnerNames(supportType: 106));
        var wrongTypeContext = CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true);
        var wrongTypePartner = FindTrainingPartner(wrongTypeContext, TrainIndex.Speed, "ライス");

        RequireEqual("[智]ライス80", wrongTypePartner.Name, "Wrong-type support card name");
    }
}

static void TestFriendSupportCardNameIsPlainText()
{
    EnsureHostConfigInitialized();
    using var names = new SmokeDatabaseScope(
        CreateTrainingPartnerNames(supportType: 0, supportNickname: "涼花"));

    {
        var context = CreateDisplayContext(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true);
        var friendPartner = FindTrainingPartner(context, TrainIndex.Speed, "涼花");

        RequireEqual("[友]涼花80", friendPartner.Name, "Friend support card name");
    }
}

static RamenScenarioAnalyzer.TrainingPartner FindTrainingPartner(RamenTrainingDisplayContext context, int trainIndex, string name)
    => context.Turn.CommandInfoArray
        .First(x => x.TrainIndex == trainIndex)
        .TrainingPartners
        .First(x => x.Name.Contains(name, StringComparison.Ordinal));

static BaseName[] CreateTrainingPartnerNames(
    int supportType,
    string supportNickname = "ライス",
    int supportCardId = 30001,
    int charaId = 1001,
    string charaName = "ライスシャワー",
    string charaNickname = "ライス")
    => [
        new BaseName(101, "理事長", "理事"),
        new BaseName(charaId, charaName, charaNickname),
        new SupportCardName(supportCardId, "幸せは曲がり角の向こう", supportNickname, supportType, charaId)
    ];

static void EnsureHostConfigInitialized() => SmokeConfig.Initialize();

static void TestModifierSurface()
{
    var context = CreateDisplayContext(includeDuplicatedScenarioCommands: false, charaTurn: 3);
    var builder = RamenTrainingDisplayBuilder.CreateDefault(context);
    var editor = new RamenTrainingDisplayEditor(builder);
    editor.Important.AddStyled(
        new RamenDisplaySegment("常驻修改: "),
        new RamenDisplaySegment("完整", RamenDisplayColor.Yellow));
    editor.Extra.AddText("额外修改");
    editor.Training.Modify(RamenTrain.Speed, card => card.AddText("训练卡修改"));
    editor.Extra.AddStyled(new RamenDisplaySegment("彩色片段", RamenDisplayColor.Cyan));

    var capture = CaptureDashboard(
        RamenTrainingDisplayRenderer.Render(RamenDisplaySnapshot.Create(builder)));
    RequireForeground(capture, "常驻修改: 完整", 6, new(StandardColor.BrightYellow), "modifier important row");
    RequireForeground(capture, "彩色片段", 0, new(StandardColor.BrightCyan), "modifier scenario row");
    foreach (var text in new[] { "额外修改", "训练卡修改" })
        if (!capture.Text.Contains(text, StringComparison.Ordinal))
            throw new InvalidOperationException($"Ramen modifier editor did not render '{text}'.");
}

static void TestProjectMetadataAndDependency()
{
    var projectPath = FindPluginProject();
    var project = XDocument.Load(projectPath);
    RequireEqual("true", project.Descendants("IsUraPlugin").Single().Value, "IsUraPlugin");
    RequireSequence(
        project.Descendants("InternalsVisibleTo").Select(x => x.Attribute("Include")?.Value).OfType<string>().ToArray(),
        ["RamenScenarioAnalyzerSmoke"],
        "InternalsVisibleTo");
    if (project.Descendants("PluginDependencies").Any())
        throw new InvalidOperationException("RamenScenarioAnalyzer must not declare EventLoggerPlugin.");

    var hostReference = project.Descendants("PackageReference").SingleOrDefault(x =>
        x.Attribute("Include")?.Value == "UmamusumeResponseAnalyzer" &&
        x.Attribute("Version")?.Value == "*");
    if (hostReference is null)
        throw new InvalidOperationException("RamenScenarioAnalyzer must reference UmamusumeResponseAnalyzer *.");

    var references = project.Descendants("ProjectReference")
        .Select(x => x.Attribute("Include")?.Value)
        .Where(x => x is not null)
        .ToArray();
    if (references.Any(x => x!.Contains("UmamusumeResponseAnalyzer", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("RamenScenarioAnalyzer must not reference the Host project.");
}

static void TestAnalyzerRegistrations(IApplication application)
{
    var plugin = new RamenPlugin();
    var context = new RecordingPluginContext(application);
    plugin.Initialize(context);

    var registrations = context.AnalyzerRegistry.Registrations;
    RequireCount(registrations.Count, 2, "Ramen analyzer registration count");
    if (registrations.Any(x => x.Kind is not AnalyzerKind.Response || x.Priority != 1))
        throw new InvalidOperationException("Ramen registrations must be response handlers with priority 1.");

    var common = registrations.Single(x => x.PayloadType == typeof(SingleModeRamenExecCommandResponse));
    RequireSequence(
        common.Patterns,
        [EndpointPattern.Regex("^/umamusume/single_mode_ramen/(?:change_short_cut|check_event|check_point|continue|exec_command|finish_claw_crane|gain_skills|race_end|race_entry|race_out|ramen_live|select_region|tasting|uraf_effect_apply)$")],
        "Ramen common response pattern");

    var load = registrations.Single(x => x.PayloadType == typeof(SingleModeRamenLoadResponse));
    RequireSequence(
        load.Patterns,
        [EndpointPattern.Exact("/umamusume/single_mode_ramen/load")],
        "Ramen load response pattern");

    plugin.Dispose();
}

static async ValueTask TestWorkspaceAndFullBleed(WorkspaceSmokeSession ui)
{
    var plugin = new RamenPlugin();
    Workspace? target = null;
    using var names = new SmokeDatabaseScope(CreateTrainingPartnerNames(supportType: 101));

    try
    {
        ui.Bootstrap.SwitchTo();
        var before = ui.CaptureScreen();
        var context = new RecordingPluginContext(ui.Application);
        plugin.Initialize(context);
        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap)
            || !string.Equals(before, ui.CaptureScreen(), StringComparison.Ordinal))
            throw new InvalidOperationException("Initialize changed the visible Workspace or framebuffer.");
        await context.DispatchAsync(
            typeof(GameApi.SingleModeRamen.CheckEvent),
            CreateRamenCheckEventResponse(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true));
        target = Workspace.Create("RamenScenarioAnalyzer");
        if (!ReferenceEquals(Workspace.Current, target))
            throw new InvalidOperationException("Ramen panel did not focus its Workspace.");
        var firstFrame = ui.CaptureScreen();
        if (!firstFrame.Contains("URAF", StringComparison.Ordinal))
            throw new InvalidOperationException("The real Host framebuffer does not show the Ramen panel content.");
        if (!firstFrame.Contains("[速]ライス80", StringComparison.Ordinal))
            throw new InvalidOperationException("The first CheckEvent packet was not visible in the Ramen framebuffer.");

        ui.Bootstrap.SwitchTo();
        await context.DispatchAsync(
            typeof(GameApi.SingleModeRamen.CheckEvent),
            CreateRamenCheckEventResponse(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true,
            friendship: 73));
        var currentAfterUpdate = Workspace.Current;

        target.SwitchTo();
        var updated = ui.CaptureScreen();
        currentAfterUpdate?.SwitchTo();
        if (!updated.Contains("[速]ライス73", StringComparison.Ordinal)
            || updated.Contains("[速]ライス80", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The later CheckEvent packet did not update the Ramen framebuffer.");
        }
        if (!ReferenceEquals(currentAfterUpdate, target))
            throw new InvalidOperationException("A later CheckEvent update did not focus the Ramen Workspace.");
    }
    finally
    {
        plugin.Dispose();
    }

    var published = target ?? throw new InvalidOperationException("Ramen did not create its Workspace.");
    var retained = Workspace.Create("RAMENSCENARIOANALYZER");
    retained.SwitchTo();
    if (!ReferenceEquals(retained, published)
        || ui.CaptureScreen().Contains("URAF", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Ramen Dispose must remove training without removing the original Workspace generation.");
    }

}

static async ValueTask TestRegisteredModifierLifecycle(WorkspaceSmokeSession ui)
{
    var plugin = new RamenPlugin();
    RamenTrainingDisplayPartProducer? first = null;
    RamenTrainingDisplayPartProducer? second = null;
    using var names = new SmokeDatabaseScope(CreateTrainingPartnerNames(supportType: 101));
    try
    {
        var context = new RecordingPluginContext(ui.Application);
        plugin.Initialize(context);
        await context.DispatchAsync(
            typeof(GameApi.SingleModeRamen.CheckEvent),
            CreateRamenCheckEventResponse(includeDuplicatedScenarioCommands: false));
        var target = Workspace.Create("RamenScenarioAnalyzer");
        var id = new RamenTrainingDisplayId(1, 2);
        ui.Bootstrap.SwitchTo();

        first = RamenTrainingDisplay.RegisterPartProducer("First");
        first.Update(
            id,
            (_, display) =>
                display.Extra.AddStyled(new RamenDisplaySegment("ramen-first", RamenDisplayColor.Cyan)));
        using var empty = RamenTrainingDisplay.RegisterPartProducer("Empty");
        empty.Update(id, (_, _) => { });
        second = RamenTrainingDisplay.RegisterPartProducer("Second");
        second.Update(id, (_, display) => display.Extra.AddText("ramen-second"));
        using var third = RamenTrainingDisplay.RegisterPartProducer("Third");
        third.Update(id, (_, display) => display.Extra.AddText("ramen-one-shot"));
        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
            throw new InvalidOperationException("Ramen part Update switched workspace focus.");

        if (!RamenTrainingDisplay.Show(id, switchToWorkspace: false))
            throw new InvalidOperationException("Ramen Show failed with producer parts.");
        target.SwitchTo();
        var composed = ui.CaptureScreen(200, 80);
        var firstTitleIndex = composed.IndexOf("First", StringComparison.Ordinal);
        var firstIndex = composed.IndexOf("ramen-first", StringComparison.Ordinal);
        var secondTitleIndex = composed.IndexOf("Second", StringComparison.Ordinal);
        var secondIndex = composed.IndexOf("ramen-second", StringComparison.Ordinal);
        var thirdTitleIndex = composed.IndexOf("Third", StringComparison.Ordinal);
        var oneShotIndex = composed.IndexOf("ramen-one-shot", StringComparison.Ordinal);
        if (firstTitleIndex < 0 || firstIndex <= firstTitleIndex
            || secondTitleIndex <= firstIndex || secondIndex <= secondTitleIndex
            || thirdTitleIndex <= secondIndex || oneShotIndex <= thirdTitleIndex
            || composed.Contains("Empty", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ramen Extra sections were not grouped in producer registration order.");
        }

        var beforeFailure = composed;
        using var failing = RamenTrainingDisplay.RegisterPartProducer("Failing");
        failing.Update(id, (_, _) => throw new InvalidOperationException("ramen-persistent-sentinel"));
        try
        {
            RamenTrainingDisplay.Show(id);
            throw new InvalidOperationException("Failing Ramen part did not propagate.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "ramen-persistent-sentinel")
        {
        }
        if (!string.Equals(beforeFailure, ui.CaptureScreen(200, 80), StringComparison.Ordinal))
            throw new InvalidOperationException("Failing Ramen part published a partial display.");
        failing.Dispose();

        ui.Bootstrap.SwitchTo();
        third.Dispose();
        first.Dispose();
        first = null;
        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
            throw new InvalidOperationException("Disposing a Ramen producer switched workspace focus.");
        if (!RamenTrainingDisplay.Show(id))
            throw new InvalidOperationException("Ramen Show failed after disposing producers.");
        target.SwitchTo();
        var afterUnregister = ui.CaptureScreen(200, 80);
        if (afterUnregister.Contains("ramen-first", StringComparison.Ordinal) ||
            !afterUnregister.Contains("ramen-second", StringComparison.Ordinal) ||
            afterUnregister.Contains("ramen-one-shot", StringComparison.Ordinal))
            throw new InvalidOperationException("Ramen producer disposal retained the wrong content.");
    }
    finally
    {
        second?.Dispose();
        first?.Dispose();
        plugin.Dispose();
    }
}

static async ValueTask TestLoadAnalyzerRendersTrainingPanel(WorkspaceSmokeSession ui)
{
    var plugin = new RamenPlugin();
    var target = Workspace.Create("RamenScenarioAnalyzer");

    try
    {
        var context = new RecordingPluginContext(ui.Application);
        plugin.Initialize(context);
        ui.Bootstrap.SwitchTo();
        await context.DispatchAsync(
            typeof(GameApi.SingleModeRamen.Load),
            CreateRamenLoadResponse(includeDuplicatedScenarioCommands: false));
        if (!ui.CaptureScreen().Contains("URAF", StringComparison.Ordinal))
            throw new InvalidOperationException("Load panel was not visible in the framebuffer.");
    }
    finally
    {
        plugin.Dispose();
    }

    target.SwitchTo();
    if (ui.CaptureScreen().Contains("URAF", StringComparison.Ordinal)
        || !ReferenceEquals(Workspace.Create("ramenscenarioanalyzer"), target))
    {
        throw new InvalidOperationException(
            "Load Dispose must remove training without removing the original Ramen Workspace generation.");
    }
}

static async ValueTask TestKeyedHistoryAndInput(WorkspaceSmokeSession ui)
{
    using var workingDirectory = new TempCurrentDirectory("RamenScenarioAnalyzer-history");
    var settingsDirectory = Path.Combine("PluginData", "RamenScenarioAnalyzer");
    Directory.CreateDirectory(settingsDirectory);
    var settingsPath = Path.Combine(settingsDirectory, "settings.json");
    File.WriteAllText(settingsPath, "{\"historyLimit\":4}");
    using var names = new SmokeDatabaseScope(CreateTrainingPartnerNames(supportType: 101));

    var plugin = new RamenPlugin();
    var context = new RecordingPluginContext(ui.Application);
    try
    {
        plugin.Initialize(context);
        await AnalyzeHistoryEntry(context, charaId: 11, turn: 2, friendship: 80);
        await AnalyzeHistoryEntry(context, charaId: 11, turn: 2, friendship: 73);
        await AnalyzeHistoryEntry(context, charaId: 11, turn: 3, friendship: 61);
        await AnalyzeHistoryEntry(context, charaId: 22, turn: 3, friendship: 54);

        var target = Workspace.Create("RamenScenarioAnalyzer");
        target.SwitchTo();
        var beforeLateUpdate = ui.CaptureScreen(200, 100);
        await AnalyzeHistoryEntry(context, charaId: 11, turn: 2, friendship: 73);
        var afterLateUpdate = ui.CaptureScreen(200, 100);
        if (!string.Equals(beforeLateUpdate, afterLateUpdate, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Updating a retained but unselected Ramen DisplayId changed the framebuffer.");
        }

        ui.SendKey(Key.CursorLeft);
        RequireHistoryFrame(ui, expectedFriendship: 73, 80, 61, 54);
        RequireVisibleText(ui, "训练历史 1/3");
        using var producer = RamenTrainingDisplay.RegisterPartProducer("HistoryTest");
        var hiddenId = new RamenTrainingDisplayId(22, 3);
        var publications = 0;
        var beforeHiddenUpdate = ui.CaptureScreen();
        producer.Update(
            hiddenId,
            (_, display) => display.Extra.AddText("RAMEN-STALE-PART"));
        producer.Update(
            hiddenId,
            (_, display) =>
            {
                publications++;
                display.Extra.AddText("RAMEN-TARGET-ID");
            });
        var afterHiddenUpdate = ui.CaptureScreen();
        if (!string.Equals(beforeHiddenUpdate, afterHiddenUpdate, StringComparison.Ordinal) ||
            publications != 0)
        {
            throw new InvalidOperationException(
                "Updating an unshown Ramen DisplayId changed the framebuffer or composed a part.");
        }

        if (!RamenTrainingDisplay.Show(hiddenId, switchToWorkspace: true))
            throw new InvalidOperationException("Explicit Ramen Show did not publish the target DisplayId.");
        var shownTarget = ui.CaptureScreen(200, 100);
        if (!shownTarget.Contains("RAMEN-TARGET-ID", StringComparison.Ordinal) ||
            shownTarget.Contains("RAMEN-STALE-PART", StringComparison.Ordinal) ||
            publications != 1)
        {
            throw new InvalidOperationException(
                "Ramen Show did not commit the latest target part exactly once.");
        }
        ui.SendKey(Key.CursorLeft);
        RequireHistoryFrame(ui, expectedFriendship: 73, 80, 61, 54);


        await AnalyzeHistoryEntry(context, charaId: 11, turn: 2, friendship: 71);
        RequireHistoryFrame(ui, expectedFriendship: 71, 73, 61, 54);
        ui.SendKey(Key.CursorDown);
        RequireHistoryFrame(ui, expectedFriendship: 61, 71, 54);

        await AnalyzeHistoryEntry(context, charaId: 33, turn: 5, friendship: 44);
        RequireHistoryFrame(ui, expectedFriendship: 61, 44);
        RequireVisibleText(ui, "有新的训练历史。按 → 查看最新。");
        await AnalyzeHistoryEntry(context, charaId: 44, turn: 6, friendship: 33);
        RequireHistoryFrame(ui, expectedFriendship: 61, 33);
        await AnalyzeHistoryEntry(context, charaId: 45, turn: 7, friendship: 32);
        await AnalyzeHistoryEntry(context, charaId: 46, turn: 8, friendship: 31);
        RequireHistoryFrame(ui, expectedFriendship: 31, 54, 44, 33, 32);
        ui.SendKey(Key.CursorUp);
        RequireHistoryFrame(ui, expectedFriendship: 32, 31, 33);
        ui.SendKey(Key.CursorUp);
        RequireHistoryFrame(ui, expectedFriendship: 33, 32, 31);
        ui.SendKey(Key.CursorDown);
        RequireHistoryFrame(ui, expectedFriendship: 32, 33, 31);
        ui.SendKey(Key.CursorRight);
        RequireHistoryFrame(ui, expectedFriendship: 31, 32, 33);

        var top = ui.CaptureScreen(120, 16, restore: false);
        ui.SendKey(Key.PageDown);
        var scrolled = ui.CaptureScreen(120, 16, restore: false);
        if (string.Equals(top, scrolled, StringComparison.Ordinal))
            throw new InvalidOperationException("PageDown must scroll the history-wrapped Ramen dashboard.");
        ui.SendKey(Key.Home);
        var restored = ui.CaptureScreen(120, 16, restore: false);
        if (string.Equals(scrolled, restored, StringComparison.Ordinal))
            throw new InvalidOperationException("Home must restore the history-wrapped Ramen dashboard to its start.");
        ui.CaptureScreen(120, 40, restore: false);
    }
    finally
    {
        plugin.Dispose();
    }

    File.WriteAllText(settingsPath, "{\"historyLimit\":0}");
    var disabled = new RamenPlugin();
    var disabledContext = new RecordingPluginContext(ui.Application);
    try
    {
        disabled.Initialize(disabledContext);
        await AnalyzeHistoryEntry(disabledContext, charaId: 50, turn: 7, friendship: 22);
        await AnalyzeHistoryEntry(disabledContext, charaId: 51, turn: 8, friendship: 21);
        ui.SendKey(Key.CursorUp);
        RequireHistoryFrame(ui, expectedFriendship: 21, 22);
    }
    finally
    {
        disabled.Dispose();
    }
}

static ValueTask AnalyzeHistoryEntry(
    RecordingPluginContext context,
    int charaId,
    int turn,
    int friendship)
    => context.DispatchAsync(
        typeof(GameApi.SingleModeRamen.CheckEvent),
        CreateRamenCheckEventResponse(
            includeDuplicatedScenarioCommands: false,
            includeTrainingPartners: true,
            friendship: friendship,
            charaTurn: turn,
            singleModeCharaId: charaId));

static void RequireHistoryFrame(
    WorkspaceSmokeSession ui,
    int expectedFriendship,
    params int[] absentFriendships)
{
    var frame = ui.CaptureScreen();
    var expected = $"[速]ライス{expectedFriendship}";
    if (!frame.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"History framebuffer does not contain '{expected}'.");
    foreach (var absent in absentFriendships)
    {
        var unexpected = $"[速]ライス{absent}";
        if (frame.Contains(unexpected, StringComparison.Ordinal))
            throw new InvalidOperationException($"History framebuffer unexpectedly contains '{unexpected}'.");
    }
}

static void RequireVisibleText(WorkspaceSmokeSession ui, string expected)
{
    var frame = ui.CaptureScreen();
    if (!frame.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Framebuffer does not contain '{expected}'.");
}

static RamenTrainingDisplayContext CreateDisplayContext(
    bool includeDuplicatedScenarioCommands,
    bool includeTrainingPartners = false,
    int? missingCommandFeelingRewardId = null,
    int supportCardId = 30001,
    int trainingPartnerCommandId = 101,
    int friendship = 80,
    int charaTurn = 2)
{
    var response = CreateRamenCheckEventResponse(
        includeDuplicatedScenarioCommands,
        includeTrainingPartners,
        missingCommandFeelingRewardId,
        supportCardId,
        trainingPartnerCommandId,
        friendship,
        charaTurn);
    var data = new RamenScenarioResponseData(
        response,
        response.data.chara_info,
        response.data.ramen_data_set,
        null, 
        response.data.home_info,
        response.data.unchecked_event_array,
        commandResult: null);
    var turn = new TurnInfoRamen(data);
    var trainStats = RamenTrainingStatsCalculator.CreateTrainStats(turn);
    return new(response, data, turn, trainStats, previousTurn: charaTurn - 1);
}

static SingleModeRamenExecCommandResponse CreateRamenCheckEventResponse(
    bool includeDuplicatedScenarioCommands,
    bool includeTrainingPartners = false,
    int? missingCommandFeelingRewardId = null,
    int supportCardId = 30001,
    int trainingPartnerCommandId = 101,
    int friendship = 80,
    int charaTurn = 2,
    int singleModeCharaId = 1)
    => new()
    {
        data = new()
        {
            chara_info = CreateChara(includeTrainingPartners, supportCardId, friendship, charaTurn, singleModeCharaId),
            home_info = new() { command_info_array = BaseTrainingCommands(includeTrainingPartners, trainingPartnerCommandId) },
            unchecked_event_array = [],
            ramen_data_set = CreateRamenDataSet(includeDuplicatedScenarioCommands, missingCommandFeelingRewardId),
            command_result = null!
        }
    };

static SingleModeRamenLoadResponse CreateRamenLoadResponse(
    bool includeDuplicatedScenarioCommands,
    bool includeTrainingPartners = false,
    int supportCardId = 30001,
    int trainingPartnerCommandId = 101,
    int friendship = 80)
    => new()
    {
        data = new()
        {
            single_mode_load_common = new()
            {
                chara_info = CreateChara(includeTrainingPartners, supportCardId, friendship),
                home_info = new() { command_info_array = BaseTrainingCommands(includeTrainingPartners, trainingPartnerCommandId) },
                unchecked_event_array = []
            },
            ramen_data_set = CreateRamenDataSet(includeDuplicatedScenarioCommands)
        }
    };

static SingleModeChara CreateChara(
    bool includeTrainingPartners = false,
    int supportCardId = 30001,
    int friendship = 80,
    int charaTurn = 2,
    int singleModeCharaId = 1)
    => new()
    {
        single_mode_chara_id = singleModeCharaId,
        speed = 100,
        stamina = 110,
        power = 120,
        guts = 130,
        wiz = 140,
        max_speed = 1500,
        max_stamina = 1500,
        max_power = 1500,
        max_guts = 1500,
        max_wiz = 1500,
        vital = 80,
        max_vital = 100,
        motivation = 5,
        turn = charaTurn,
        skill_point = 100,
        playing_state = 1,
        skill_array = [],
        skill_tips_array = [],
        skill_upgrade_info_array = [],
        support_card_array = includeTrainingPartners
            ? [new() { position = 1, support_card_id = supportCardId }]
            : [],
        evaluation_info_array = includeTrainingPartners
            ? [new() { target_id = 1, evaluation = friendship }, new() { target_id = 101, evaluation = 40 }]
            : [],
        training_level_info_array =
        [
            new() { command_id = 101, level = 5 },
            new() { command_id = 105, level = 4 },
            new() { command_id = 102, level = 3 },
            new() { command_id = 103, level = 2 },
            new() { command_id = 106, level = 1 }
        ]
    };

static SingleModeRamenDataSet CreateRamenDataSet(
    bool includeDuplicatedScenarioCommands,
    int? missingCommandFeelingRewardId = null)
    => new()
    {
        command_info_array = includeDuplicatedScenarioCommands
            ? [.. ScenarioTrainingCommands(), .. ScenarioTrainingCommands(601, 602, 603, 604, 605)]
            : ScenarioTrainingCommands(),
        special_feeling_num = 2,
        command_feeling_info_array =
        [
            new() { command_type = 1, command_id = 101, feeling_id = 1 },
            new() { command_type = 1, command_id = 105, feeling_id = 2 },
            new() { command_type = 1, command_id = 102, feeling_id = 3 },
            new() { command_type = 1, command_id = 103, feeling_id = 3 },
            new() { command_type = 1, command_id = 106, feeling_id = 2 },
            new() { command_type = 1, command_id = 601, feeling_id = 0 },
            new() { command_type = 1, command_id = 602, feeling_id = 0 },
            new() { command_type = 1, command_id = 603, feeling_id = 0 },
            new() { command_type = 1, command_id = 604, feeling_id = 0 },
            new() { command_type = 1, command_id = 605, feeling_id = 0 }
        ],
        training_exec_info_array = [new() { base_command_id = 101, exec_count = 3 }],
        feeling_reduce_turn_info_array = CommandFeelingTurnArray(missingCommandFeelingRewardId),
        feeling_turn_info_array = FeelingRewardValues((1, 7), (2, 1), (3, 4)),
        feeling_info_array = [],
        active_effect_array = [new() { effect_category = 1, effect_id = 2, effect_value = 3 }],
        uraf_effect_info = new() { uraf_effect_type = 4, uraf_effect_state = 5 }
    };

static SingleModeRamenFeelingReduceTurnInfo[] CommandFeelingTurnArray(int? missingCommandFeelingRewardId)
    =>
    [
        .. new[]
        {
            CommandFeelingTurns(101, (1, 9), (2, 5), (3, 5)),
            CommandFeelingTurns(105, (1, 4), (2, 4), (3, 3)),
            CommandFeelingTurns(102, (1, 4), (2, 3), (3, 4)),
            CommandFeelingTurns(103, (1, 4), (2, 3), (3, 7)),
            CommandFeelingTurns(106, (1, 6), (2, 7), (3, 5))
        }.Where(x => x.command_id != missingCommandFeelingRewardId)
    ];

static SingleModeRamenFeelingReduceTurnInfo CommandFeelingTurns(
    int commandId,
    params (int FeelingId, int Turn)[] values)
    => new()
    {
        command_type = 1,
        command_id = commandId,
        feeling_turn_array =
        [
            .. values.Select(x => new SingleModeRamenReduceFeelingTurn
            {
                feeling_id = x.FeelingId,
                turn = x.Turn
            })
        ]
    };

static SingleModeRamenFeelingTurnInfo[] FeelingRewardValues(params (int FeelingId, int RewardValue)[] values)
    => [.. values.Select(x => new SingleModeRamenFeelingTurnInfo
    {
        feeling_id = x.FeelingId,
        remain_turn = x.RewardValue
    })];

static SingleModeCommandInfo[] BaseTrainingCommands(
    bool includeTrainingPartners = false,
    int trainingPartnerCommandId = 101)
    => [.. new[] { 101, 105, 102, 103, 106 }.Select(commandId => new SingleModeCommandInfo
    {
        command_type = 1,
        command_id = commandId,
        is_enable = 1,
        training_partner_array = includeTrainingPartners && commandId == trainingPartnerCommandId ? [1, 101] : [],
        tips_event_partner_array = [],
        params_inc_dec_info_array = TrainingParams(commandId),
        failure_rate = commandId == 103 ? 20 : 0
    })];

static SingleModeRamenCommandInfo[] ScenarioTrainingCommands(params int[] commandIds)
{
    if (commandIds.Length == 0)
        commandIds = [101, 105, 102, 103, 106];

    return [.. commandIds.Select(commandId => new SingleModeRamenCommandInfo
    {
        command_type = 1,
        command_id = commandId,
        params_inc_dec_info_array = RamenScenarioTrainingParams(commandId)
    })];
}

static SingleModeParamsIncDecInfo[] RamenScenarioTrainingParams(int commandId)
    => commandId switch
    {
        101 => [new() { target_type = 1, value = 7 }, new() { target_type = 3, value = 2 }, new() { target_type = 30, value = 8 }],
        105 => [new() { target_type = 2, value = 3 }, new() { target_type = 4, value = 1 }, new() { target_type = 30, value = 1 }],
        102 => [new() { target_type = 2, value = 1 }, new() { target_type = 3, value = 1 }, new() { target_type = 30, value = 1 }],
        103 => [new() { target_type = 1, value = 1 }, new() { target_type = 3, value = 1 }, new() { target_type = 4, value = 4 }, new() { target_type = 30, value = 5 }],
        106 => [new() { target_type = 1, value = 1 }, new() { target_type = 5, value = 4 }, new() { target_type = 30, value = 6 }],
        601 => [new() { target_type = 1, value = 7 }, new() { target_type = 3, value = 2 }, new() { target_type = 30, value = 8 }],
        602 => [new() { target_type = 2, value = 3 }, new() { target_type = 4, value = 1 }, new() { target_type = 30, value = 1 }],
        603 => [new() { target_type = 2, value = 1 }, new() { target_type = 3, value = 1 }, new() { target_type = 30, value = 1 }],
        604 => [new() { target_type = 1, value = 1 }, new() { target_type = 3, value = 1 }, new() { target_type = 4, value = 4 }, new() { target_type = 30, value = 5 }],
        605 => [new() { target_type = 1, value = 1 }, new() { target_type = 5, value = 4 }, new() { target_type = 30, value = 6 }],
        _ => throw new InvalidOperationException($"Unknown Ramen scenario command: {commandId}")
    };

static SingleModeParamsIncDecInfo[] TrainingParams(int commandId)
    => [new() { target_type = 1 + (Array.IndexOf(new[] { 101, 105, 102, 103, 106, 601, 602, 603, 604, 605 }, commandId) % 5), value = 10 },
        new() { target_type = 30, value = 5 }];

static string Render(WorkspaceContent content)
{
    return CaptureDashboard(content).Text;
}

static DashboardCapture CaptureDashboard(
    WorkspaceContent content,
    int width = 120,
    int height = 36)
{
    using var application = Terminal.Gui.App.Application.Create(new VirtualTimeProvider())
        .Init(DriverRegistry.Names.ANSI);
    application.Driver!.SetScreenSize(width, height);
    using var window = new Window
    {
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        BorderStyle = null
    };
    using var view = content.CreateView();
    view.Width = Dim.Fill();
    view.Height = Dim.Fill();
    window.Add(view);

    DashboardCapture? capture = null;
    Exception? callbackFailure = null;
    application.AddTimeout(TimeSpan.Zero, () =>
    {
        try
        {
            application.Driver!.SetScreenSize(width, height);
            application.LayoutAndDraw(forceRedraw: true);
            capture = Capture(application, view, width, height);
            application.RequestStop(window);
        }
        catch (Exception ex)
        {
            callbackFailure = ex;
            application.RequestStop(window);
        }

        return false;
    });
    RunApplication(application, window, nameof(CaptureDashboard));

    if (callbackFailure is not null)
        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
    return capture ?? throw new InvalidOperationException("Terminal.Gui framebuffer was not captured.");
}

static DashboardResizeCapture CaptureResizeSequence(WorkspaceContent content)
{
    using var application = Terminal.Gui.App.Application.Create(new VirtualTimeProvider())
        .Init(DriverRegistry.Names.ANSI);
    using var window = new Window
    {
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        BorderStyle = null
    };
    using var view = content.CreateView();
    view.Width = Dim.Fill();
    view.Height = Dim.Fill();
    window.Add(view);

    DashboardResizeCapture? capture = null;
    Exception? callbackFailure = null;
    application.AddTimeout(TimeSpan.Zero, () =>
    {
        try
        {
            application.Driver!.SetScreenSize(120, 36);
            application.LayoutAndDraw(forceRedraw: true);
            var wide = Capture(application, view, 120, 36);

            application.Driver.SetScreenSize(80, 30);
            application.LayoutAndDraw(forceRedraw: true);
            var narrow = Capture(application, view, 80, 30);

            view.ScrollHorizontal(view.GetContentSize().Width);
            application.LayoutAndDraw(forceRedraw: true);
            var narrowScrolled = Capture(application, view, 80, 30);

            application.Driver.SetScreenSize(120, 36);
            application.LayoutAndDraw(forceRedraw: true);
            var restored = Capture(application, view, 120, 36);

            capture = new(wide, narrow, narrowScrolled, restored);
            application.RequestStop(window);
        }
        catch (Exception ex)
        {
            callbackFailure = ex;
            application.RequestStop(window);
        }

        return false;
    });
    RunApplication(application, window, nameof(CaptureResizeSequence));

    if (callbackFailure is not null)
        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
    return capture ?? throw new InvalidOperationException("Terminal.Gui resize sequence was not captured.");
}

static DashboardVerticalScrollCapture CaptureVerticalScrollSequence(WorkspaceContent content)
{
    using var application = Terminal.Gui.App.Application.Create(new VirtualTimeProvider())
        .Init(DriverRegistry.Names.ANSI);
    using var window = new Window
    {
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        BorderStyle = null
    };
    using var view = content.CreateView();
    view.Width = Dim.Fill();
    view.Height = Dim.Fill();
    window.Add(view);

    DashboardVerticalScrollCapture? capture = null;
    Exception? callbackFailure = null;
    application.AddTimeout(TimeSpan.Zero, () =>
    {
        try
        {
            application.Driver!.SetScreenSize(120, 36);
            application.LayoutAndDraw(forceRedraw: true);
            var initial = Capture(application, view, 120, 36);

            if (!RamenTrainingDisplayRenderer.TryScroll(view, Command.PageDown))
                throw new InvalidOperationException("Ramen dashboard rejected PageDown.");
            application.LayoutAndDraw(forceRedraw: true);
            var pageDown = Capture(application, view, 120, 36);

            if (!RamenTrainingDisplayRenderer.TryScroll(view, Command.End))
                throw new InvalidOperationException("Ramen dashboard rejected End.");
            application.LayoutAndDraw(forceRedraw: true);
            capture = new(initial, pageDown, Capture(application, view, 120, 36));
            application.RequestStop(window);
        }
        catch (Exception ex)
        {
            callbackFailure = ex;
            application.RequestStop(window);
        }

        return false;
    });
    RunApplication(application, window, nameof(CaptureVerticalScrollSequence));

    if (callbackFailure is not null)
        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
    return capture ?? throw new InvalidOperationException("Terminal.Gui vertical-scroll sequence was not captured.");
}

static void RunApplication(IApplication application, Window window, string helper)
{
    const int timeoutSeconds = 10;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    Console.Error.WriteLine($"[Ramen smoke UI] {helper}: start");
    try
    {
        application.RunAsync(window, timeout.Token).GetAwaiter().GetResult();
    }
    catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
    {
        Console.Error.WriteLine($"[Ramen smoke UI] {helper}: timeout");
        throw new TimeoutException($"{helper} timed out after {timeoutSeconds} seconds.", ex);
    }

    if (timeout.IsCancellationRequested)
    {
        Console.Error.WriteLine($"[Ramen smoke UI] {helper}: timeout");
        throw new TimeoutException($"{helper} timed out after {timeoutSeconds} seconds.");
    }

    Console.Error.WriteLine($"[Ramen smoke UI] {helper}: complete");
}

static DashboardCapture Capture(IApplication application, View view, int width, int height)
{
    var cells = application.Driver!.Contents
        ?? throw new InvalidOperationException("Terminal.Gui framebuffer was not initialized.");
    return new(
        (Cell[,])cells.Clone(),
        new(0, 0, Math.Min(width, cells.GetLength(1)), Math.Min(height, cells.GetLength(0))),
        view.FrameToScreen(),
        view.Viewport,
        view.GetContentSize(),
        view.HorizontalScrollBar.Visible,
        view.HorizontalScrollBar.Value,
        view.VerticalScrollBar.Visible,
        view.VerticalScrollBar.Value);
}

static void RequireForeground(
    DashboardCapture capture,
    string token,
    int graphemeIndex,
    TColor expected,
    string name)
{
    var point = FindCellToken(capture, token).Points[graphemeIndex];
    var attribute = capture.Cells[point.Y, point.X].Attribute
        ?? throw new InvalidOperationException($"{name} has no attribute.");
    RequireEqual(expected, attribute.Foreground, $"{name} foreground");
    RequireEqual(TColor.Black, attribute.Background, $"{name} background");
}

static void RequireBlackBackground(DashboardCapture capture, Point point, string name)
{
    var actual = capture.Cells[point.Y, point.X].Attribute?.Background
        ?? throw new InvalidOperationException($"{name} has no background attribute.");
    RequireEqual(TColor.Black, actual, $"{name} background");
}

static void RequireBorderForeground(
    DashboardCapture capture,
    Rectangle frame,
    TColor expected,
    string name)
{
    var attribute = capture.Cells[frame.Top, frame.Left].Attribute
        ?? throw new InvalidOperationException($"{name} has no border attribute.");
    RequireEqual(expected, attribute.Foreground, $"{name} foreground");
    RequireEqual(TColor.Black, attribute.Background, $"{name} background");
}

static TColor BorderForeground(DashboardCapture capture, Rectangle frame)
{
    return capture.Cells[frame.Top, frame.Left].Attribute?.Foreground
        ?? throw new InvalidOperationException("Training border has no foreground attribute.");
}

static CellToken FindCellToken(DashboardCapture capture, string token)
{
    var elements = new List<string>();
    var enumerator = StringInfo.GetTextElementEnumerator(token);
    while (enumerator.MoveNext())
        elements.Add((string)enumerator.Current);

    for (var y = capture.Region.Top; y < capture.Region.Bottom; y++)
    {
        for (var x = capture.Region.Left; x < capture.Region.Right; x++)
        {
            var points = new List<Point>(elements.Count);
            var column = x;
            var matches = true;
            foreach (var element in elements)
            {
                if (column >= capture.Region.Right ||
                    capture.Cells[y, column].Grapheme != element)
                {
                    matches = false;
                    break;
                }

                points.Add(new(column, y));
                column += Math.Max(1, element.GetColumns());
            }

            if (matches)
                return new(points);
        }
    }

    throw new InvalidOperationException($"Expected framebuffer token '{token}' was not found.");
}

static string RenderRows(IEnumerable<string> rows)
    => string.Join(Environment.NewLine, rows);

static string FindPluginProject()
    => Path.Combine(Environment.GetEnvironmentVariable("URA_TEST_PLUGIN_ROOT")
        ?? throw new InvalidOperationException("URA_TEST_PLUGIN_ROOT must identify the plugin checkout."),
        "RamenScenarioAnalyzer.csproj");

static T Require<T>(T? value, string name)
    where T : class
    => value ?? throw new InvalidOperationException($"{name} was not found.");

static void RequireCount(int actual, int expected, string name)
{
    if (actual != expected)
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
}

static void RequireEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
}

static void RequireSequence<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, string name)
{
    if (!actual.SequenceEqual(expected))
        throw new InvalidOperationException($"{name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
}

sealed record DashboardCapture(
    Cell[,] Cells,
    Rectangle Region,
    Rectangle RootFrame,
    Rectangle RootViewport,
    Size RootContentSize,
    bool HorizontalScrollBarVisible,
    int HorizontalScrollBarValue,
    bool VerticalScrollBarVisible,
    int VerticalScrollBarValue)
{
    public string Text
    {
        get
        {
            var output = new StringBuilder();
            for (var y = Region.Top; y < Region.Bottom; y++)
            {
                var line = new StringBuilder();
                for (var x = Region.Left; x < Region.Right; x++)
                {
                    var grapheme = Cells[y, x].Grapheme;
                    line.Append(grapheme);
                    x += Math.Max(1, grapheme.GetColumns()) - 1;
                }
                output.AppendLine(line.ToString().TrimEnd());
            }

            return output.ToString().TrimEnd();
        }
    }
}

sealed record DashboardResizeCapture(
    DashboardCapture Wide,
    DashboardCapture Narrow,
    DashboardCapture NarrowScrolled,
    DashboardCapture Restored);

sealed record DashboardVerticalScrollCapture(
    DashboardCapture Initial,
    DashboardCapture PageDown,
    DashboardCapture Scrolled);

sealed record CellToken(IReadOnlyList<Point> Points);

static class SmokeConfig
{
    static bool initialized;

    public static void Initialize()
    {
        if (initialized)
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"ura-ramen-config-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            Directory.SetCurrentDirectory(directory);
            UraConfig.Initialize();
            if (!File.Exists(Path.Combine(directory, "config.yaml")))
                throw new InvalidOperationException("Config.Initialize did not create config.yaml.");
            initialized = true;
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(directory, recursive: true);
        }
    }
}

sealed class SmokeDatabaseScope : IDisposable
{
    readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ura-ramen-smoke-{Guid.NewGuid():N}");
    bool disposed;

    public SmokeDatabaseScope(IReadOnlyList<BaseName> names)
    {
        Directory.CreateDirectory(dataDirectory);
        try
        {
            Write("events_male.br", Array.Empty<Story>());
            Write("events_female.br", Array.Empty<Story>());
            Write("skill_data.br", Array.Empty<UmamusumeResponseAnalyzer.Entities.SkillData>());
            Write("skill_upgrade_speciality.br", Array.Empty<SkillUpgradeSpeciality>());
            Write("talent_skill_sets.br", new Dictionary<int, TalentSkillData[]>());
            Write("factor_ids.br", new Dictionary<int, string>());
            Write("wins_saddle.br", Array.Empty<int>());
            Write("succession_relation.br", new SuccessionRelationTable());
            Publish(names);
        }
        catch
        {
            Directory.Delete(dataDirectory, recursive: true);
            throw;
        }
    }

    public void Publish(IReadOnlyList<BaseName> names)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Write(
            "names.br",
            names.ToList(),
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                ContractResolver = WritablePropertiesContractResolver.Instance
            });

        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dataDirectory);
            var availability = UraDatabase.Initialize().GetAwaiter().GetResult();
            if (availability is not UmamusumeResponseAnalyzer.DatabaseAvailability.Ready)
                throw new InvalidOperationException($"Smoke database availability: expected Ready, got {availability}.");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        try
        {
            Publish([]);
        }
        finally
        {
            disposed = true;
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    void Write<T>(string fileName, T value, JsonSerializerSettings? settings = null)
    {
        using var file = File.Create(Path.Combine(dataDirectory, fileName));
        using var brotli = new BrotliStream(file, CompressionLevel.Optimal);
        using var writer = new StreamWriter(brotli, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(JsonConvert.SerializeObject(value, settings));
    }
}

sealed class WritablePropertiesContractResolver : DefaultContractResolver
{
    public static WritablePropertiesContractResolver Instance { get; } = new();

    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        => [.. base.CreateProperties(type, memberSerialization).Where(x => x.Writable)];
}

static class TrainIndex
{
    public const int Speed = 1;
    public const int Power = 3;
}

sealed class RecordingPluginContext(IApplication application) : IPluginContext
{
    public IApplication Application => application;
    public IPluginHostEvents Events { get; } = new ThrowingPluginHostEvents();
    public RecordingPluginAnalyzerRegistry AnalyzerRegistry { get; } = new();
    public IPluginAnalyzerRegistry Analyzers => AnalyzerRegistry;
    public bool IsPluginAvailable(string internalName) => false;

    public void RunBackground(Func<CancellationToken, ValueTask> operation)
        => throw new NotSupportedException("Ramen smoke does not use background operations.");

    public ValueTask DispatchAsync<TPayload>(Type endpointType, TPayload payload)
        => AnalyzerRegistry.DispatchAsync(endpointType, payload);
}

sealed class ThrowingPluginHostEvents : IPluginHostEvents
{
    public void OnStarted(Func<CancellationToken, ValueTask> handler)
        => throw new NotSupportedException("Ramen smoke does not use host events.");
}

sealed record AnalyzerRegistration(
    Type PayloadType,
    AnalyzerKind Kind,
    IReadOnlyList<EndpointPattern> Patterns,
    int Priority,
    Func<GameEndpointDescriptor, object, ValueTask> Handler);

sealed class RecordingPluginAnalyzerRegistry : IPluginAnalyzerRegistry
{
    static readonly GameHttpHeaders EmptyHeaders = new(null, null, null, null, null, null);

    public List<AnalyzerRegistration> Registrations { get; } = [];

    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0)
    {
        Registrations.Add(new(
            typeof(TPayload),
            kind,
            [.. patterns],
            priority,
            (endpoint, payload) => handler(new(endpoint, (TPayload)payload, EmptyHeaders))));
    }

    public ValueTask DispatchAsync<TPayload>(Type endpointType, TPayload payload)
    {
        var registration = Registrations.Single(x => x.PayloadType == typeof(TPayload));
        return registration.Handler(GameEndpointCatalog.ByEndpointType[endpointType], payload!);
    }
}
