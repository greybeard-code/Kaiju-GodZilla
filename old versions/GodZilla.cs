#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.GreyBeard.KingPanaZilla;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using System.Windows.Controls;
using SharpDX;
// SharpDX collides with WPF on Brush/Color/FontWeight/FontStyle - alias per AGENTS.md
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontWeight = SharpDX.DirectWrite.FontWeight;
using FontStyle = SharpDX.DirectWrite.FontStyle;
using NewsPrintLocation = NinjaTrader.NinjaScript.Indicators.Playr101.NewsSignals.NewsPrintLocation;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies.Playr101
{
    #region GUI Categories
    [CategoryOrder ("Strategy Information", 0)]
    [CategoryOrder ("ATM Parameters", 1)]
    [CategoryOrder ("Display", 2)]
    [CategoryOrder ("Signals", 3)]
    [CategoryOrder ("Group Triggers", 4)]
    [CategoryOrder ("Risk Management", 5)]
    [CategoryOrder ("News Filter", 6)]
    [CategoryOrder ("Session Parameters", 7)]
    [CategoryOrder ("Indicator Settings", 8)]
    [CategoryOrder ("Indicator: KingOrderBlock", 9)]
    [CategoryOrder ("Indicator: PANAKanal", 10)]
    [CategoryOrder ("Indicator: ThunderZilla", 11)]
    [CategoryOrder ("Indicator: SumoPullback", 12)]
    [CategoryOrder ("Indicator: SuperJumpBoost", 13)]
    [CategoryOrder ("Indicator: Visuals", 14)]
    [CategoryOrder ("Logging", 15)]
    #endregion

    public class GodZilla : Strategy, ICustomTypeDescriptor
    {
        public override string DisplayName => Name;

        // Enums — nested inside the class so GodZilla.cs is fully self-contained
        // and does not conflict with GodZillaKilla.cs (which defines identical copies).
        public enum HudCorner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Center,
            Hidden
        }

        public enum GodZillaHudSize
        {
            Tiny,
            Small,
            Normal,
            Large,
            Huge
        }

        #region Variables
        // Drawing
        private SimpleFont title = new SimpleFont("Agency Fb", 16) { Size = 20, Bold = true };

        // ATM
        private string atmStrategyId = string.Empty;
        private string orderId = string.Empty;
        private bool isAtmStrategyCreated = false;

        // Internal tracker
        private double dailyRealizedPnL = 0.0;
        private double dailyUnrealizedPnL = 0.0;
        private double totalRealizedPnL = 0.0;
        private double sessionStartTotalRealizedPnL = 0.0;
        private double totalRunningPnL = 0.0;
        private double lastAtmRealizedPnL = 0.0;
        private bool dailyLimitHit = false;
        private string dailyPnlStatusMessage = string.Empty;

        // Naked-position watchdog (wall-clock throttled — playback time is unreliable in fast-forward)
        private DateTime lastNakedCheckUtc = DateTime.MinValue;
        private const int NakedCheckIntervalSeconds = 3;

        // Wall-clock throttle for tick-series UI work. Without this, fast-forward
        // playback drives OnBarUpdate(BIP=1) hundreds of times per wall-second and
        // the dispatcher queue grows faster than the UI thread can drain it →
        // visible lockup ~3-4 minutes in. Limit-hit / PnL boundary checks still
        // fire every tick (they MUST), but UI snapshot+invalidate are throttled.
        private DateTime _lastTickUiSyncUtc = DateTime.MinValue;
        private const int TICK_UI_SYNC_INTERVAL_MS = 100;

        // Trade logging
        private StreamWriter _logWriter;
        private Dictionary<string, TradeRecord> _tradeMap = new Dictionary<string, TradeRecord>();
        private bool _atmPositionConfirmed = false;

        private class TradeRecord
        {
            public string Trigger;
            public string Direction;
            public DateTime OpenTime;
            public string Instrument;
            public double OpenPrice;
            public int Qty;
            public string AtmStrategyName;
        }

        // Signal tracking
        private class SignalTradeStats
        {
            public int TotalTrades;
            public int LongTrades;
            public int ShortTrades;
            public int Winners;
            public int Losers;
        }

        private SignalTradeStats panaZilliaStats = new SignalTradeStats();
        private SignalTradeStats kingZillaStats = new SignalTradeStats();
        private SignalTradeStats kingPanaStats = new SignalTradeStats();
        private SignalTradeStats sumoStats = new SignalTradeStats();
        private SignalTradeStats sjbStats = new SignalTradeStats();

        private bool activeTradeUsesPanaZillia = false;
        private bool activeTradeUsesKingZilla = false;
        private bool activeTradeUsesKingPana = false;
        private bool activeTradeUsesSumo = false;
        private bool activeTradeUsesSjb = false;
        private MarketPosition activeTradeDirection = MarketPosition.Flat;
        private string lastTradeClosedSummary = string.Empty;

        private string _strategyVersion = "";
        private string Credits = "";

        // Indicators
        private gbKingPanaZilla _gbIndicator;
        private gbSumoPullback _sumo;
        private gbSuperJumpBoost _sjb;

        // ---- HUD (SharpDX boxed dashboard, AlightenLite-style) ----
        // Snapshot fields written by data thread (BuildDashboardSnapshot) and
        // read by UI thread (OnRender). Strings are immutable so a torn read
        // gives stale-but-consistent text rather than corrupt frames.
        private string _hudTitle      = "GodZilla";
        private string _hudVersion    = "";
        private string _hudArm        = "";
        private string _hudSession    = "";
        private string _hudNews       = "";
        private string _hudPnl        = "";
        private string _hudPnlOpen    = "";
        private string _hudTargets    = "";
        private string _hudStatus     = "";
        private string _hudLastTrade  = "";
        private List<string> _hudSignalLines = new List<string>();
        private bool   _hudPnlPositive = true;
        private bool   _hudSessionOutside = false;
        private bool   _hudKillHit = false;
        private bool   _hudKillProfit = false;
        private bool   _hudNewsBlocked = false;
        private bool   _hudShowNews = false;
        private bool   _hudShowSignals = false;
        private string _hudKillBanner = "";

        // SharpDX brush + format (UI thread owned, recreated on RT change)
        private SharpDX.DirectWrite.TextFormat       _dashFormat;
        private SharpDX.DirectWrite.TextFormat       _dashTitleFormat;
        private SharpDX.Direct2D1.SolidColorBrush    _bTextWhite;
        private SharpDX.Direct2D1.SolidColorBrush    _bTextDim;
        private SharpDX.Direct2D1.SolidColorBrush    _bTextGreen;
        private SharpDX.Direct2D1.SolidColorBrush    _bTextRed;
        private SharpDX.Direct2D1.SolidColorBrush    _bTextYellow;
        private SharpDX.Direct2D1.SolidColorBrush    _bTextOrange;
        private SharpDX.Direct2D1.SolidColorBrush    _bTextCyan;
        private SharpDX.Direct2D1.SolidColorBrush    _bBackground;
        private SharpDX.Direct2D1.SolidColorBrush    _bBorder;
        private SharpDX.Direct2D1.SolidColorBrush    _bBorderHot;

        // Same-target-skip per AGENTS.md lifecycle defense rule
        // _dxInitialized must gate both the same-target-skip AND the lazy re-init
        // in OnRender — otherwise NT8 firing OnRenderTargetChanged with the same
        // RT during playback transitions can null brushes and the next OnRender's
        // lazy init may fail silently in its own try/catch, leaving the dashboard
        // dead for the rest of the session (per feedback_nt8_lifecycle_defense.md).
        private SharpDX.Direct2D1.RenderTarget _lastSeenRenderTarget;
        private bool _dxInitialized;
        private int _hudErrors;
        private bool _hudFirstRenderLogged;

        // Tracks last-applied DashboardSize so EnsureDashboardFonts() can detect
        // a runtime size change and rebuild only the font formats (cheaper than
        // tearing down all brushes — they're size-independent).
        private GodZillaHudSize _lastSizeApplied = (GodZillaHudSize)(-1);

        // Throttled invalidate to avoid UI starvation in playback
        private DateTime _lastHudInvalidateUtc = DateTime.MinValue;
        private const int HUD_MIN_INVALIDATE_MS = 100;

        // EMA Filter
        private EMA _emaShortFilter;
        private EMA _emaLongFilter;

        // News Filter
        private NinjaTrader.NinjaScript.Indicators.Playr101.NewsSignals newsIndicator;
        private bool _lastNewsBlockActive = false;
        private bool _newsRuntimeDisabledPrinted = false;

        // Button Panel
        private Border _controlPanel;
        private Button _armLongBtn, _armShortBtn, _autoArmBtn, _closeBtn;
        private Label _statusLabel;
        private bool _uiInitialized = false;
        private bool _armLong = true;
        private bool _armShort = true;
        private bool _autoArm = true;
        private volatile bool _strategyEnabled = true;
        #endregion

        protected override void OnStateChange ()
        {
            if (State == State.SetDefaults)
            {
                Description = "GodZilla — composite strategy combining gbKingPanaZilla (KingOrderBlock + PANAKanal + ThunderZilla), gbSumoPullback, and gbSuperJumpBoost signals.";
                Name = "GodZilla";
                StrategyName = Name;
                _strategyVersion = "1.0.1";

                Author = "Playr101";
                Credits = "GreyBeard, ninZa.co, RenkoKings, ES, rbro999";

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = true;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Day;
                TraceOrders = true;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                BarsRequiredToTrade = 2;
                IsInstantiatedOnEachOptimizationIteration = false;
                IsUnmanaged = false;
                IsAdoptAccountPositionAware = true;
                // NOTE: NT8 strategies continue trading while the chart tab is hidden
                // (only OnRender is paused — OnBarUpdate, position management, and ATM
                // calls keep firing on the data thread). No `IsSuspendedWhileInactive`
                // toggle exists on the Strategy class; the SharpDX HUD below never
                // gates trading because it only reads snapshot strings on the UI thread.

                //--------- User Configurable Parameters ---------//
                // ATM Strategy
                AtmStrategy = string.Empty;

                // Signal Usage
                // Display panels — defaults: both visible at the standard corners.
                // Set ShowDashboard = false (or DashboardPosition = Hidden) to disable
                // the SharpDX info panel entirely (stability mode — see AGENTS.md
                // gotcha #17). The control panel can also be turned off independently.
                ShowDashboard         = true;
                DashboardPosition     = HudCorner.BottomRight;
                DashboardSize         = GodZillaHudSize.Normal;
                ShowControlPanel      = true;
                ControlPanelPosition  = HudCorner.TopLeft;

                EnableSignalTracking = false;
                UsePanaZilliaSignals = true;
                UseKingZillaSignals = true;
                UseKingPanaSignals = true;
                UseSumoPullbackSignals = false;
                UseSuperJumpBoostSignals = false;

                // Rolling Group entry triggers (defaults: off — fall back to OR-mode)
                EnableGroup2Trigger  = false;
                EnableGroup3Trigger  = false;
                EnableGroup4Trigger  = false;
                EnableGroup5Trigger  = false;
                RollingWindowBars    = 5;

                // EMA Filter
                UseEmaFilter = false;
                EmaShortPeriod = 21;
                EmaLongPeriod = 50;

                // Logging
                LogEnabled = false;
                EnableDebug = false;

                // Risk Defaults
                UseUnrealizedPnl = true;
                UseDailyProfitTarget = false;
                DailyProfitTarget = 500;
                UseDailyLossLimit = true;
                DailyLossLimit = 200;

                // News Filter Defaults
                EnableNewsFilter = false;
                NewsFlattenAtWarningTime = false;

                NewsShowDisplay = true;
                NewsDisplayLocation = NewsPrintLocation.TopRight;
                NewsDisplayXOffsetPixels = 20;
                NewsDisplayYOffsetPixels = 60;
                NewsUse24HourTime = false;
                NewsShowBackground = true;
                NewsShowTimeBackBrush = false;
                NewsTimeBackBrush = Brushes.DimGray;

                NewsUSOnlyEvents = true;
                NewsTodaysNewsOnly = true;
                NewsShowLowPriority = false;
                NewsMaxNewsItems = 10;
                NewsRefreshInterval = 15;

                NewsPreBlockMinutes = 5;
                NewsPostBlockMinutes = 5;
                NewsBlockHighImpact = true;
                NewsBlockMediumImpact = true;
                NewsBlockLowImpact = false;

                NewsSendAlerts = true;
                NewsAlertInterval = 15;
                NewsAlertWavFileName = "Alert1.wav";

                NewsDefaultTextColor = Brushes.White;
                NewsWarningTextColor = Brushes.Yellow;
                var newsBg = new System.Windows.Media.SolidColorBrush (System.Windows.Media.Color.FromArgb (170, 0, 0, 0));
                newsBg.Freeze ();
                NewsBackgroundColor = newsBg;
                NewsHeaderColor = Brushes.White;
                NewsHighImpactColor = Brushes.Red;
                NewsMediumImpactColor = Brushes.DarkGreen;
                NewsLowImpactColor = Brushes.Blue;
                NewsDefaultFont = new SimpleFont ("Arial", 10);
                NewsWarningFont = new SimpleFont ("Arial", 10) { Bold = true, Italic = true };
                NewsDebug = false;

                // Session Times
                EnableTF1 = true;
                StartTime1 = DateTime.Parse ("19:00", System.Globalization.CultureInfo.InvariantCulture);
                EndTime1 = DateTime.Parse ("23:30", System.Globalization.CultureInfo.InvariantCulture);
                FlattenTF1 = false;
                EnableTF2 = true;
                StartTime2 = DateTime.Parse ("03:00", System.Globalization.CultureInfo.InvariantCulture);
                EndTime2 = DateTime.Parse ("09:00", System.Globalization.CultureInfo.InvariantCulture);
                FlattenTF2 = false;
                EnableTF3 = true;
                StartTime3 = DateTime.Parse ("08:00", System.Globalization.CultureInfo.InvariantCulture);
                EndTime3 = DateTime.Parse ("03:45", System.Globalization.CultureInfo.InvariantCulture);
                FlattenTF3 = true;
                EnableSkipTimeWindow = true;
                SkipStartTime = DateTime.Parse ("11:45", System.Globalization.CultureInfo.InvariantCulture);
                SkipEndTime = DateTime.Parse ("13:00", System.Globalization.CultureInfo.InvariantCulture);

                // Indicator Settings master toggle
                ShowIndicatorSettings = false;

                // KingOrderBlock indicator defaults (mirror gbKingPanaZilla)
                King_SwingPointNeighborhood = 5;
                King_ImbalanceQualifying = 3;
                King_OrderBlockFindingBosChochPeriod = 50;
                King_OrderBlockAge = 500;
                King_OrderBlocksSameDirectionOffset = 10;
                King_OrderBlocksDifferenceDirectionOffset = 10;
                King_SignalTradeQuantityPerOrderBlock = 3;
                King_SignalTradeSplitBars = 6;

                // PANAKanal indicator defaults
                Pana_Period = 20;
                Pana_Factor = 4.0;
                Pana_MiddlePeriod = 14;
                Pana_SignalBreakSplit = 20;
                Pana_SignalPullbackFindingPeriod = 10;

                // ThunderZilla indicator defaults
                Thunder_TrendMAType = gbThunderZillaMAType.SMA;
                Thunder_TrendPeriod = 100;
                Thunder_TrendSmoothingEnabled = false;
                Thunder_TrendSmoothingMethod = gbThunderZillaMAType.EMA;
                Thunder_TrendSmoothingPeriod = 10;
                Thunder_StopOffsetMultiplierStop = 60.0;
                Thunder_SignalQuantityPerFlat = 2;
                Thunder_SignalQuantityPerTrend = 999;

                // SumoPullback indicator defaults (mirror gbSumoPullback)
                Sumo_SlowMAType                = gbSumoPullbackMAType.SMA;
                Sumo_SlowMAPeriod              = 60;
                Sumo_SlowMASmoothingEnabled    = false;
                Sumo_SlowMASmoothingMethod     = gbSumoPullbackMAType.EMA;
                Sumo_SlowMASmoothingPeriod     = 10;
                Sumo_FastMA1Type               = gbSumoPullbackMAType.EMA;
                Sumo_FastMA1Period             = 14;
                Sumo_FastMA1SmoothingEnabled   = false;
                Sumo_FastMA1SmoothingMethod    = gbSumoPullbackMAType.SMA;
                Sumo_FastMA1SmoothingPeriod    = 5;
                Sumo_FastMA2Type               = gbSumoPullbackMAType.EMA;
                Sumo_FastMA2Period             = 30;
                Sumo_FastMA2SmoothingEnabled   = false;
                Sumo_FastMA2SmoothingMethod    = gbSumoPullbackMAType.SMA;
                Sumo_FastMA2SmoothingPeriod    = 10;
                Sumo_FastMA3Type               = gbSumoPullbackMAType.EMA;
                Sumo_FastMA3Period             = 45;
                Sumo_FastMA3SmoothingEnabled   = false;
                Sumo_FastMA3SmoothingMethod    = gbSumoPullbackMAType.SMA;
                Sumo_FastMA3SmoothingPeriod    = 15;
                Sumo_SignalSplitFirst          = 15;
                Sumo_SignalSplitSecond         = 30;

                // SuperJumpBoost indicator defaults (mirror gbSuperJumpBoost)
                SJB_SensitiveModeEnabled       = true;
                SJB_OffsetLevel1               = 1.0;
                SJB_OffsetLevel2               = 2.0;
                SJB_OffsetLevel3               = 3.0;
                SJB_OffsetLevel4               = 4.0;
                SJB_OffsetBase                 = 4.0;
                SJB_ReferencePricePeriod       = 2;
                SJB_LineLevelsOffset           = 100;
                SJB_ExtremeNeighborhood        = 30;
                SJB_SignalCloseThreshold       = 70;
                SJB_SignalQuantityPerZone      = 2;
                SJB_SignalSplit                = 20;

                // Indicator visual defaults
                PanaZilliaBrush = Brushes.Cyan;
                KingZillaBrush = Brushes.DodgerBlue;
                KingPanaBrush = Brushes.LimeGreen;
                SumoBrush = Brushes.Orange;
                SjbBrush = Brushes.Magenta;
                ArrowOffset = 3;
            }
            else if (State == State.Configure)
            {
                ClearOutputWindow ();
                AddDataSeries (BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                // Use the factory call exactly as before, then push strategy values onto
                // the indicator. Property assignments land before the framework advances
                // the indicator to State.DataLoaded, so the child indicators (_king,
                // _pana, _thunder) are constructed with our values.
                _gbIndicator = gbKingPanaZilla ();

                _gbIndicator.King_SwingPointNeighborhood = King_SwingPointNeighborhood;
                _gbIndicator.King_ImbalanceQualifying = King_ImbalanceQualifying;
                _gbIndicator.King_OrderBlockFindingBosChochPeriod = King_OrderBlockFindingBosChochPeriod;
                _gbIndicator.King_OrderBlockAge = King_OrderBlockAge;
                _gbIndicator.King_OrderBlocksSameDirectionOffset = King_OrderBlocksSameDirectionOffset;
                _gbIndicator.King_OrderBlocksDifferenceDirectionOffset = King_OrderBlocksDifferenceDirectionOffset;
                _gbIndicator.King_SignalTradeQuantityPerOrderBlock = King_SignalTradeQuantityPerOrderBlock;
                _gbIndicator.King_SignalTradeSplitBars = King_SignalTradeSplitBars;

                _gbIndicator.Pana_Period = Pana_Period;
                _gbIndicator.Pana_Factor = Pana_Factor;
                _gbIndicator.Pana_MiddlePeriod = Pana_MiddlePeriod;
                _gbIndicator.Pana_SignalBreakSplit = Pana_SignalBreakSplit;
                _gbIndicator.Pana_SignalPullbackFindingPeriod = Pana_SignalPullbackFindingPeriod;

                _gbIndicator.Thunder_TrendMAType = Thunder_TrendMAType;
                _gbIndicator.Thunder_TrendPeriod = Thunder_TrendPeriod;
                _gbIndicator.Thunder_TrendSmoothingEnabled = Thunder_TrendSmoothingEnabled;
                _gbIndicator.Thunder_TrendSmoothingMethod = Thunder_TrendSmoothingMethod;
                _gbIndicator.Thunder_TrendSmoothingPeriod = Thunder_TrendSmoothingPeriod;
                _gbIndicator.Thunder_StopOffsetMultiplierStop = Thunder_StopOffsetMultiplierStop;
                _gbIndicator.Thunder_SignalQuantityPerFlat = Thunder_SignalQuantityPerFlat;
                _gbIndicator.Thunder_SignalQuantityPerTrend = Thunder_SignalQuantityPerTrend;

                _gbIndicator.PanaZilliaBrush = PanaZilliaBrush;
                _gbIndicator.KingZillaBrush = KingZillaBrush;
                _gbIndicator.KingPanaBrush = KingPanaBrush;
                _gbIndicator.ArrowOffset = ArrowOffset;

                AddChartIndicator (_gbIndicator);
                _gbIndicator.Name = "";

                // gbSumoPullback (always built so child cache stays warm; entry path gated by UseSumoPullbackSignals)
                _sumo = gbSumoPullback (
                    Sumo_SlowMAType,
                    Sumo_SlowMAPeriod,
                    Sumo_SlowMASmoothingEnabled,
                    Sumo_SlowMASmoothingMethod,
                    Sumo_SlowMASmoothingPeriod,
                    Sumo_FastMA1Type,
                    Sumo_FastMA1Period,
                    Sumo_FastMA1SmoothingEnabled,
                    Sumo_FastMA1SmoothingMethod,
                    Sumo_FastMA1SmoothingPeriod,
                    Sumo_FastMA2Type,
                    Sumo_FastMA2Period,
                    Sumo_FastMA2SmoothingEnabled,
                    Sumo_FastMA2SmoothingMethod,
                    Sumo_FastMA2SmoothingPeriod,
                    Sumo_FastMA3Type,
                    Sumo_FastMA3Period,
                    Sumo_FastMA3SmoothingEnabled,
                    Sumo_FastMA3SmoothingMethod,
                    Sumo_FastMA3SmoothingPeriod,
                    Sumo_SignalSplitFirst,
                    Sumo_SignalSplitSecond);
                AddChartIndicator (_sumo);
                _sumo.Name = "";

                // gbSuperJumpBoost
                _sjb = gbSuperJumpBoost (
                    SJB_SensitiveModeEnabled,
                    SJB_OffsetLevel1,
                    SJB_OffsetLevel2,
                    SJB_OffsetLevel3,
                    SJB_OffsetLevel4,
                    SJB_OffsetBase,
                    SJB_ReferencePricePeriod,
                    SJB_LineLevelsOffset,
                    SJB_ExtremeNeighborhood,
                    SJB_SignalCloseThreshold,
                    SJB_SignalQuantityPerZone,
                    SJB_SignalSplit);
                AddChartIndicator (_sjb);
                _sjb.Name = "";

                // Optional EMA trade filter
                if (UseEmaFilter)
                {
                    _emaShortFilter = EMA (EmaShortPeriod);
                    AddChartIndicator (_emaShortFilter);
                    _emaShortFilter.Name = "";
                    _emaShortFilter.Plots[0].Brush = Brushes.DodgerBlue;
                    _emaShortFilter.Plots[0].Width = 2;

                    _emaLongFilter = EMA (EmaLongPeriod);
                    AddChartIndicator (_emaLongFilter);
                    _emaLongFilter.Name = "";
                    _emaLongFilter.Plots[0].Brush = Brushes.HotPink;
                    _emaLongFilter.Plots[0].Width = 2;
                }

                // Optional News Filter - live chart only
                if (EnableNewsFilter)
                {
                    if (IsNewsFilterRuntimeDisabledContext ())
                    {
                        newsIndicator = null;

                        if (!_newsRuntimeDisabledPrinted)
                        {
                            if (EnableDebug)
                                Print ($"[{Name}] News Filter disabled for this runtime context. It is live-chart only and will not run in Strategy Analyzer/backtest or Playback/Market Replay.");
                            _newsRuntimeDisabledPrinted = true;
                        }
                    }
                    else
                    {
                        newsIndicator = NewsSignals (
                            NewsShowDisplay,                    // ShowNewsDisplay
                            NewsDisplayLocation,                // DisplayLocation
                            NewsDisplayXOffsetPixels,           // DisplayXOffsetPixels
                            NewsDisplayYOffsetPixels,           // DisplayYOffsetPixels
                            NewsUse24HourTime,                  // Use24timeFormat
                            NewsShowBackground,                 // ShowBackground
                            NewsShowTimeBackBrush,              // ShowNewsTimeBackBrush
                            NewsTimeBackBrush,                  // NewsTimeBackBrush
                            NewsUSOnlyEvents,                   // USOnlyEvents
                            NewsTodaysNewsOnly,                 // TodaysNewsOnly
                            NewsShowLowPriority,                // ShowLowPriority
                            NewsMaxNewsItems,                   // MaxNewsItems
                            NewsRefreshInterval,                // NewsRefreshInterval
                            NewsPreBlockMinutes,                // PreNewsBlockMinutes
                            NewsPostBlockMinutes,               // PostNewsBlockMinutes
                            NewsBlockHighImpact,                // BlockHighImpact
                            NewsBlockMediumImpact,              // BlockMediumImpact
                            NewsBlockLowImpact,                 // BlockLowImpact
                            NewsSendAlerts,                     // SendAlerts
                            NewsAlertInterval,                  // AlertInterval
                            NewsAlertWavFileName,               // AlertWavFileName
                            NewsDefaultTextColor,               // DefaultTextColor
                            NewsWarningTextColor,               // WarningTextColor
                            NewsBackgroundColor,                // BackgroundColor
                            NewsHeaderColor,                    // HeaderColor
                            NewsHighImpactColor,                // HighPriorityColor
                            NewsMediumImpactColor,              // MediumPriorityColor
                            NewsLowImpactColor,                 // LowPriorityColor
                            NewsDefaultFont,                    // DefaultFont
                            NewsWarningFont,                    // WarningFont
                            NewsDebug                           // Debug
                        );

                        AddChartIndicator (newsIndicator);
                        newsIndicator.Name = "";
                    }
                }

                if (LogEnabled)
                {
                    // Sanitize the account name so it's safe to embed in a file path.
                    string accountName = (Account != null && !string.IsNullOrEmpty (Account.Name)) ? Account.Name : "NoAccount";
                    string safeAccount = string.Concat (accountName.Split (System.IO.Path.GetInvalidFileNameChars ())).Replace (" ", "_");

                    string logPath = Path.Combine(
                        NinjaTrader.Core.Globals.UserDataDir,
                        "GodZilla_" + safeAccount + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                    _logWriter = new StreamWriter (logPath, append: false, encoding: Encoding.UTF8);
                    _logWriter.WriteLine ("OpenTime,Account,Instrument,OpenPrice,Qty,CloseTime,Trigger,Direction,AtmStrategy,RealizedPnL");
                    _logWriter.Flush ();
                }

                if (CurrentBar >= 0)
                    DrawPnlDisplay ();
            }
            else if (State == State.Realtime)
            {
                sessionStartTotalRealizedPnL = totalRealizedPnL;
                lastAtmRealizedPnL = 0.0;
                dailyRealizedPnL = 0.0;
                dailyUnrealizedPnL = 0.0;
                totalRunningPnL = totalRealizedPnL;
                dailyLimitHit = false;
                dailyPnlStatusMessage = string.Empty;
                lastTradeClosedSummary = string.Empty;
                _atmPositionConfirmed = false;

                _strategyEnabled = true;
                _armLong = true;
                _armShort = true;
                _autoArm = true;

                if (EnableDebug)
                    Print ($"{Name} entered realtime. ATM mode active.");

                if (ChartControl != null && !_uiInitialized)
                    CreateRBroControlPanel ();

                UpdateRBroButtons ();
                UpdateRBroStatusUI ();
            }
            else if (State == State.Terminated)
            {
                try
                {
                    if (_logWriter != null)
                    {
                        _logWriter.Flush ();
                        _logWriter.Dispose ();
                        _logWriter = null;
                    }
                }
                catch { }

                try { RemoveRBroControlPanel (); } catch { }
                try { DisposeSharpDxResources (); } catch { }
            }
        }

        protected override void OnBarUpdate ()
        {
            if (_gbIndicator == null || State == State.Historical)
                return;

            if (BarsInProgress == 1)
            {
                if (CurrentBars[1] < 1)
                    return;

                UpdateDailyPnlOnTickSeries ();
                return;
            }

            if (BarsInProgress != 0)
                return;

            if (CurrentBars[0] < BarsRequiredToTrade)
                return;

            // Flatten logic at end of TimeFrames
            int currentTime = ToTime(Time[0]);
            CheckFlattenTimeframes (currentTime);

            // News filter management/flatten check
            ManageNewsFilter ();

            if (dailyLimitHit)
                return;

            bool isWithinTradingTime = CheckTradingTimeframes(currentTime);

            if (Position.MarketPosition != MarketPosition.Flat)
                return; // Do not enter new trades while in a position

            if (!isWithinTradingTime || State != State.Realtime)
                return;

            // Honor the on-chart enable/disable button — block new entries when off.
            if (!_strategyEnabled)
                return;

            // Block new entries during the active news warning/post-news window.
            if (IsNewsTradingBlocked ())
                return;

            // Entry Logic
            double pz = _gbIndicator.PanaZilla_Trade[0];
            double kz = _gbIndicator.KingZilla_Trade[0];
            double kp = _gbIndicator.KingPana_Trade[0];
            // gbSumoPullback Signal_Trade: +1 bullish, -1 bearish, 0 none
            double sumoSig = (_sumo != null) ? _sumo.Signal_Trade[0] : 0;
            // gbSuperJumpBoost Signal_Trade can be +/-1, +/-2 — treat sign only
            double sjbSig = (_sjb != null) ? _sjb.Signal_Trade[0] : 0;

            bool panaLong = pz == 1 && UsePanaZilliaSignals;
            bool kingZillaLong = kz == 1 && UseKingZillaSignals;
            bool kingPanaLong = kp == 1 && UseKingPanaSignals;
            bool sumoLong = sumoSig > 0 && UseSumoPullbackSignals;
            bool sjbLong = sjbSig > 0 && UseSuperJumpBoostSignals;
            bool panaShort = pz == -1 && UsePanaZilliaSignals;
            bool kingZillaShort = kz == -1 && UseKingZillaSignals;
            bool kingPanaShort = kp == -1 && UseKingPanaSignals;
            bool sumoShort = sumoSig < 0 && UseSumoPullbackSignals;
            bool sjbShort = sjbSig < 0 && UseSuperJumpBoostSignals;

            bool goLong, goShort;
            int  groupTriggeredSize = 0;       // 0 = single-signal mode; 2/3/4/5 = N-of-pool consensus

            // Rolling group consensus mode: when ANY of EnableGroup3/4/5 is on,
            // entries require N selected indicators (Use*Signals = true) to agree
            // on direction within the last RollingWindowBars bars. The largest
            // satisfied group wins. Single-signal triggers are bypassed in this mode.
            bool groupModeActive = EnableGroup2Trigger || EnableGroup3Trigger || EnableGroup4Trigger || EnableGroup5Trigger;

            if (groupModeActive)
            {
                int win = Math.Max (1, RollingWindowBars);
                int longAgree = 0, shortAgree = 0;
                int poolSize = 0;

                if (UsePanaZilliaSignals)
                {
                    poolSize++;
                    double s = LatestSignalInWindow (_gbIndicator.PanaZilla_Trade, win);
                    if (s > 0) longAgree++; else if (s < 0) shortAgree++;
                }
                if (UseKingZillaSignals)
                {
                    poolSize++;
                    double s = LatestSignalInWindow (_gbIndicator.KingZilla_Trade, win);
                    if (s > 0) longAgree++; else if (s < 0) shortAgree++;
                }
                if (UseKingPanaSignals)
                {
                    poolSize++;
                    double s = LatestSignalInWindow (_gbIndicator.KingPana_Trade, win);
                    if (s > 0) longAgree++; else if (s < 0) shortAgree++;
                }
                if (UseSumoPullbackSignals)
                {
                    poolSize++;
                    double s = (_sumo != null) ? LatestSignalInWindow (_sumo.Signal_Trade, win) : 0;
                    if (s > 0) longAgree++; else if (s < 0) shortAgree++;
                }
                if (UseSuperJumpBoostSignals)
                {
                    poolSize++;
                    double s = (_sjb != null) ? LatestSignalInWindow (_sjb.Signal_Trade, win) : 0;
                    if (s > 0) longAgree++; else if (s < 0) shortAgree++;
                }

                bool gLong = false, gShort = false;
                int  gSize = 0;

                // Largest threshold satisfied wins (5 > 4 > 3). Group threshold of N
                // requires at least N agreeing AND at least N selected in pool.
                if (EnableGroup5Trigger && poolSize >= 5 && longAgree  >= 5) { gLong  = true; gSize = 5; }
                else if (EnableGroup4Trigger && poolSize >= 4 && longAgree  >= 4) { gLong  = true; gSize = 4; }
                else if (EnableGroup3Trigger && poolSize >= 3 && longAgree  >= 3) { gLong  = true; gSize = 3; }
                else if (EnableGroup2Trigger && poolSize >= 2 && longAgree  >= 2) { gLong  = true; gSize = 2; }

                if (EnableGroup5Trigger && poolSize >= 5 && shortAgree >= 5) { gShort = true; gSize = 5; }
                else if (EnableGroup4Trigger && poolSize >= 4 && shortAgree >= 4) { gShort = true; gSize = 4; }
                else if (EnableGroup3Trigger && poolSize >= 3 && shortAgree >= 3) { gShort = true; gSize = 3; }
                else if (EnableGroup2Trigger && poolSize >= 2 && shortAgree >= 2) { gShort = true; gSize = 2; }

                goLong  = gLong;
                goShort = gShort;
                groupTriggeredSize = gSize;

                // In group mode, attribute the trade to ALL selected indicators
                // (each is part of the consensus). The per-bar single-signal flags
                // remain as-is for the trigger string (PZ/KZ/KP/SUMO/SJB).
                if (gLong)
                {
                    panaLong       = UsePanaZilliaSignals;
                    kingZillaLong  = UseKingZillaSignals;
                    kingPanaLong   = UseKingPanaSignals;
                    sumoLong       = UseSumoPullbackSignals;
                    sjbLong        = UseSuperJumpBoostSignals;
                }
                if (gShort)
                {
                    panaShort      = UsePanaZilliaSignals;
                    kingZillaShort = UseKingZillaSignals;
                    kingPanaShort  = UseKingPanaSignals;
                    sumoShort      = UseSumoPullbackSignals;
                    sjbShort       = UseSuperJumpBoostSignals;
                }
            }
            else
            {
                // Single-signal OR-mode (any selected indicator triggers).
                goLong  = panaLong  || kingZillaLong  || kingPanaLong  || sumoLong  || sjbLong;
                goShort = panaShort || kingZillaShort || kingPanaShort || sumoShort || sjbShort;
            }

            // Optional EMA trade filter: longs require short EMA > long EMA, shorts require short EMA < long EMA.
            if (UseEmaFilter && _emaShortFilter != null && _emaLongFilter != null && CurrentBar >= Math.Max (EmaShortPeriod, EmaLongPeriod))
            {
                bool bullishEmaAlignment = _emaShortFilter[0] > _emaLongFilter[0];
                bool bearishEmaAlignment = _emaShortFilter[0] < _emaLongFilter[0];

                if (goLong && !bullishEmaAlignment)
                {
                    goLong = false;
                    panaLong = kingZillaLong = kingPanaLong = sumoLong = sjbLong = false;
                }
                if (goShort && !bearishEmaAlignment)
                {
                    goShort = false;
                    panaShort = kingZillaShort = kingPanaShort = sumoShort = sjbShort = false;
                }
            }

            // Submit ATM entry only when both ids are reset
            if (orderId.Length == 0 && atmStrategyId.Length == 0 && goLong && _armLong)
            {
                string trigger = string.Join("+", new[] {
                    panaLong ? "PZ" : null,
                    kingZillaLong ? "KZ" : null,
                    kingPanaLong ? "KP" : null,
                    sumoLong ? "SUMO" : null,
                    sjbLong ? "SJB" : null,
                }.Where(s => s != null));
                if (groupTriggeredSize > 0) trigger = "G" + groupTriggeredSize + ":" + trigger;

                isAtmStrategyCreated = false;
                SetActiveTradeSignalSources (panaLong, kingZillaLong, kingPanaLong, sumoLong, sjbLong, MarketPosition.Long);
                atmStrategyId = GetAtmStrategyUniqueId ();
                orderId = GetAtmStrategyUniqueId ();
                _tradeMap[atmStrategyId] = new TradeRecord
                {
                    Trigger = trigger,
                    Direction = "Long",
                    OpenTime = Time[0],
                    Instrument = FormatInstrumentName (),
                    OpenPrice = 0.0,
                    Qty = 0,
                    AtmStrategyName = AtmStrategy
                };
                AtmStrategyCreate (OrderAction.Buy, OrderType.Market, 0, 0, TimeInForce.Day, orderId, AtmStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
                {
                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                        isAtmStrategyCreated = true;
                });
            }
            else if (orderId.Length == 0 && atmStrategyId.Length == 0 && goShort && _armShort)
            {
                string trigger = string.Join("+", new[] {
                    panaShort ? "PZ" : null,
                    kingZillaShort ? "KZ" : null,
                    kingPanaShort ? "KP" : null,
                    sumoShort ? "SUMO" : null,
                    sjbShort ? "SJB" : null,
                }.Where(s => s != null));
                if (groupTriggeredSize > 0) trigger = "G" + groupTriggeredSize + ":" + trigger;

                isAtmStrategyCreated = false;
                SetActiveTradeSignalSources (panaShort, kingZillaShort, kingPanaShort, sumoShort, sjbShort, MarketPosition.Short);
                atmStrategyId = GetAtmStrategyUniqueId ();
                orderId = GetAtmStrategyUniqueId ();
                _tradeMap[atmStrategyId] = new TradeRecord
                {
                    Trigger = trigger,
                    Direction = "Short",
                    OpenTime = Time[0],
                    Instrument = FormatInstrumentName (),
                    OpenPrice = 0.0,
                    Qty = 0,
                    AtmStrategyName = AtmStrategy
                };
                AtmStrategyCreate (OrderAction.SellShort, OrderType.Market, 0, 0, TimeInForce.Day, orderId, AtmStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
                {
                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                        isAtmStrategyCreated = true;
                });
            }

            if (!isAtmStrategyCreated)
                return;

            if (orderId.Length > 0)
            {
                string[] status = GetAtmStrategyEntryOrderStatus(orderId);

                if (status != null && status.Length > 0)
                {
                    if (EnableDebug)
                        Print ("The entry order average fill price is: " + status[0]);
                    if (EnableDebug)
                        Print ("The entry order filled amount is: " + status[1]);
                    if (EnableDebug)
                        Print ("The entry order order state is: " + status[2]);

                    if (status[2] == "Filled")
                    {
                        CaptureFill (status);
                        orderId = string.Empty;
                    }
                    else if (status[2] == "Cancelled" || status[2] == "Rejected")
                    {
                        orderId = string.Empty;
                        ClearActiveTradeSignalSources ();
                    }
                }
            }

            if (atmStrategyId.Length > 0)
            {
                if (EnableDebug)
                    Print ("The current ATM Strategy market position is: " + GetAtmStrategyMarketPosition (atmStrategyId));
                if (EnableDebug)
                    Print ("The current ATM Strategy position quantity is: " + GetAtmStrategyPositionQuantity (atmStrategyId));
                if (EnableDebug)
                    Print ("The current ATM Strategy average price is: " + GetAtmStrategyPositionAveragePrice (atmStrategyId));
                if (EnableDebug)
                    Print ("The current ATM Strategy Unrealized PnL is: " + GetAtmStrategyUnrealizedProfitLoss (atmStrategyId));
            }
        }

        private bool IsNewsFilterRuntimeDisabledContext ()
        {
            // Strategy Analyzer/backtest usually has no chart context.
            if (ChartControl == null)
                return true;

            // Disable in Playback/Market Replay.
            if (IsPlaybackConnectionActive ())
                return true;

            return false;
        }

        private bool IsNewsFilterRuntimeActive ()
        {
            if (!EnableNewsFilter)
                return false;

            if (newsIndicator == null)
                return false;

            if (State != State.Realtime)
                return false;

            if (IsPlaybackConnectionActive ())
                return false;

            return true;
        }

        private bool IsPlaybackConnectionActive ()
        {
            try
            {
                Type connectionType = typeof (NinjaTrader.Cbi.Connection);

                System.Reflection.PropertyInfo playbackProp = connectionType.GetProperty (
            "PlaybackConnection",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (playbackProp != null)
                {
                    object playbackConnection = playbackProp.GetValue (null, null);

                    if (playbackConnection != null)
                        return true;
                }

                if (Account != null && Account.Connection != null && Account.Connection.Options != null)
                {
                    string provider = Account.Connection.Options.Provider.ToString ();

                    if (!string.IsNullOrEmpty (provider)
                        && provider.IndexOf ("Playback", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private bool IsNewsTradingBlocked ()
        {
            try
            {
                if (!IsNewsFilterRuntimeActive ())
                    return false;

                if (newsIndicator.NewsBlock == null)
                    return false;

                return newsIndicator.NewsBlock[0] >= 0.5;
            }
            catch
            {
                return false;
            }
        }

        private double GetNewsMinutesToNext ()
        {
            try
            {
                if (!IsNewsFilterRuntimeActive ())
                    return -1.0;

                if (newsIndicator.MinutesToNextNews == null)
                    return -1.0;

                return newsIndicator.MinutesToNextNews[0];
            }
            catch
            {
                return -1.0;
            }
        }

        private void ManageNewsFilter ()
        {
            if (!IsNewsFilterRuntimeActive ())
            {
                _lastNewsBlockActive = false;
                return;
            }

            bool newsBlocked = IsNewsTradingBlocked ();
            double minutesToNext = GetNewsMinutesToNext ();

            bool enteringPreNewsWarningWindow =
        newsBlocked
        && !_lastNewsBlockActive
        && minutesToNext >= 0;

            if (enteringPreNewsWarningWindow && NewsFlattenAtWarningTime)
                FlattenEverything ("News warning window started - flatten/cancel enabled");

            _lastNewsBlockActive = newsBlocked;
        }

        private string BuildNewsFilterDisplayLine ()
        {
            if (!EnableNewsFilter)
                return "News Filter: OFF";

            if (!IsNewsFilterRuntimeActive ())
                return "News Filter: DISABLED";

            bool blocked = IsNewsTradingBlocked ();
            double mins = GetNewsMinutesToNext ();

            if (blocked)
                return mins >= 0
                    ? "News Filter: BLOCKED | Next: " + mins.ToString ("F1") + " min"
                    : "News Filter: BLOCKED | Post-News";

            return mins >= 0
                ? "News Filter: CLEAR | Next: " + mins.ToString ("F1") + " min"
                : "News Filter: CLEAR";
        }

        private string FormatInstrumentName ()
        {
            string name = Instrument.MasterInstrument.Name;
            if (Instrument.MasterInstrument.InstrumentType == InstrumentType.Future
                && Instrument.Expiry != Core.Globals.MaxDate)
                name += " " + Instrument.Expiry.ToString ("MM-yy");
            return name;
        }

        private void CaptureFill (string[] status)
        {
            if (!_tradeMap.TryGetValue (atmStrategyId, out TradeRecord rec))
                return;

            if (rec.OpenPrice == 0.0
                && double.TryParse (status[0], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double price)
                && price > 0)
                rec.OpenPrice = Instrument.MasterInstrument.RoundToTickSize (price);

            if (rec.Qty == 0
                && int.TryParse (status[1], out int qty))
                rec.Qty = qty;
        }

        private void UpdateDailyPnlOnTickSeries ()
        {
            if (State != State.Realtime)
                return;

            DateTime tickTime = Times[1][0];

            ManageNewsFilter ();

            if (Bars.IsFirstBarOfSession)
            {
                if (EnableDebug)
                    Print ($"{tickTime:yyyy-MM-dd HH:mm:ss} | DAILY PNL RESET | "
                        + $"Bars.IsFirstBarOfSession={Bars.IsFirstBarOfSession} | "
                        + $"TotalRealizedBeforeReset={totalRealizedPnL:F2}");

                sessionStartTotalRealizedPnL = totalRealizedPnL;
                lastAtmRealizedPnL = 0.0;
                dailyRealizedPnL = 0.0;
                dailyUnrealizedPnL = 0.0;
                totalRunningPnL = totalRealizedPnL;
                dailyLimitHit = false;
                dailyPnlStatusMessage = string.Empty;
            }

            if (orderId.Length > 0)
            {
                string[] status = GetAtmStrategyEntryOrderStatus (orderId);

                if (status != null && status.Length >= 3)
                {
                    if (status[2] == "Filled")
                    {
                        CaptureFill (status);
                        orderId = string.Empty;
                    }
                    else if (status[2] == "Cancelled" || status[2] == "Rejected")
                    {
                        orderId = string.Empty;
                        ClearActiveTradeSignalSources ();
                    }
                }
                // If status is null the order may not be registered yet — leave orderId set
                // and let the position check below detect the fill via GetAtmStrategyMarketPosition.
            }

            if (atmStrategyId.Length > 0)
            {
                Cbi.MarketPosition atmPos = GetAtmStrategyMarketPosition (atmStrategyId);

                if (atmPos != Cbi.MarketPosition.Flat)
                {
                    // Position is live — entry filled regardless of orderId / callback state.
                    _atmPositionConfirmed = true;
                    isAtmStrategyCreated = true;
                    orderId = string.Empty;

                    if (_tradeMap.TryGetValue (atmStrategyId, out TradeRecord rec) && rec.OpenPrice == 0.0)
                    {
                        double avgPrice = GetAtmStrategyPositionAveragePrice (atmStrategyId);
                        if (avgPrice > 0)
                        {
                            rec.OpenPrice = Instrument.MasterInstrument.RoundToTickSize (avgPrice);
                            rec.Qty = (int)GetAtmStrategyPositionQuantity (atmStrategyId);
                        }
                    }

                    dailyUnrealizedPnL = Instrument.MasterInstrument.RoundToTickSize (GetAtmStrategyUnrealizedProfitLoss (atmStrategyId));
                }
                else if (_atmPositionConfirmed)
                {
                    // Was open, now flat — trade closed normally.
                    double atmRealized = GetAtmStrategyRealizedProfitLoss (atmStrategyId);

                    if (!double.IsNaN (atmRealized))
                        lastAtmRealizedPnL = Instrument.MasterInstrument.RoundToTickSize (atmRealized);
                    else
                        lastAtmRealizedPnL = 0.0;

                    totalRealizedPnL += lastAtmRealizedPnL;
                    UpdateSignalTrackingOnTradeClose (lastAtmRealizedPnL);
                    UpdateLastTradeClosedSummary (lastAtmRealizedPnL);
                    PrintSignalTrackingOnTradeClose (lastAtmRealizedPnL);

                    if (_logWriter != null && _tradeMap.TryGetValue (atmStrategyId, out TradeRecord tr))
                    {
                        string acct = (Account != null && !string.IsNullOrEmpty (Account.Name)) ? Account.Name : "NoAccount";
                        _logWriter.WriteLine ($"{tr.OpenTime:yyyy-MM-dd HH:mm:ss},{acct},{tr.Instrument},{tr.OpenPrice:F2},{tr.Qty},{tickTime:yyyy-MM-dd HH:mm:ss},{tr.Trigger},{tr.Direction},{tr.AtmStrategyName},{lastAtmRealizedPnL:F2}");
                        _logWriter.Flush ();
                    }

                    _tradeMap.Remove (atmStrategyId);
                    ClearActiveTradeSignalSources ();
                    atmStrategyId = string.Empty;
                    orderId = string.Empty;
                    isAtmStrategyCreated = false;
                    _atmPositionConfirmed = false;
                    dailyUnrealizedPnL = 0.0;
                }
                else
                {
                    // Position not yet confirmed open and shows flat — ATM still registering.
                    // Suppress ATM method calls to avoid error spam during registration window.
                    dailyUnrealizedPnL = 0.0;
                }
            }
            else
                dailyUnrealizedPnL = 0.0;

            dailyRealizedPnL = totalRealizedPnL - sessionStartTotalRealizedPnL;
            totalRunningPnL = totalRealizedPnL + (UseUnrealizedPnl ? dailyUnrealizedPnL : 0.0);

            double dailyPnlToCheck = dailyRealizedPnL + (UseUnrealizedPnl ? dailyUnrealizedPnL : 0.0);

            if (EnableDebug)
                Print ($"{tickTime:yyyy-MM-dd HH:mm:ss} | DAILY PNL CHECK | "
                    + $"Closed={dailyRealizedPnL:F2} | "
                    + $"Open={dailyUnrealizedPnL:F2} | "
                    + $"Check={dailyPnlToCheck:F2} | "
                    + $"TotalRunning={totalRunningPnL:F2} | "
                    + $"PTEnabled={UseDailyProfitTarget} | "
                    + $"PT={DailyProfitTarget:F2} | "
                    + $"LLEnabled={UseDailyLossLimit} | "
                    + $"LL={DailyLossLimit:F2} | "
                    + $"LimitHit={dailyLimitHit} | "
                    + $"ATM={atmStrategyId} | "
                    + $"Order={orderId}");

            if (!dailyLimitHit)
            {
                if (UseDailyProfitTarget && dailyPnlToCheck >= DailyProfitTarget)
                {
                    dailyLimitHit = true;
                    dailyPnlStatusMessage = $"DAILY PROFIT TARGET HIT: {dailyPnlToCheck:C}";

                    if (EnableDebug)
                        Print ($"{tickTime:yyyy-MM-dd HH:mm:ss} | DAILY PROFIT TARGET HIT | "
                            + $"Check={dailyPnlToCheck:F2} >= PT={DailyProfitTarget:F2} | "
                            + $"Closed={dailyRealizedPnL:F2} | Open={dailyUnrealizedPnL:F2} | "
                            + $"Calling FlattenAll()");

                    FlattenAll ();
                }
                else if (UseDailyLossLimit && dailyPnlToCheck <= -DailyLossLimit)
                {
                    dailyLimitHit = true;
                    dailyPnlStatusMessage = $"DAILY LOSS LIMIT HIT: {dailyPnlToCheck:C}";

                    if (EnableDebug)
                        Print ($"{tickTime:yyyy-MM-dd HH:mm:ss} | DAILY LOSS LIMIT HIT | "
                            + $"Check={dailyPnlToCheck:F2} <= LL=-{DailyLossLimit:F2} | "
                            + $"Closed={dailyRealizedPnL:F2} | Open={dailyUnrealizedPnL:F2} | "
                            + $"Calling FlattenAll()");

                    FlattenAll ();
                }
            }

            // Naked-position check + dashboard refresh are wall-clock throttled.
            // Both are O(N) work (Account.Orders enumeration, SharpDX snapshot,
            // dispatcher InvokeAsync) and don't need per-tick fidelity. Without
            // throttling, fast-forward playback queues thousands of dispatcher
            // jobs per wall-second → UI thread lockup.
            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - lastNakedCheckUtc).TotalSeconds >= NakedCheckIntervalSeconds)
            {
                lastNakedCheckUtc = nowUtc;
                CheckForNakedPositions (tickTime);
            }
            if ((nowUtc - _lastTickUiSyncUtc).TotalMilliseconds >= TICK_UI_SYNC_INTERVAL_MS)
            {
                _lastTickUiSyncUtc = nowUtc;
                DrawPnlDisplay ();
                UpdateRBroStatusUI ();
            }
        }

        // ============================================================
        //  HUD — SharpDX boxed dashboard (AlightenLite-style)
        //
        //  Data thread (OnBarUpdate) builds the snapshot strings here.
        //  UI thread (OnRender) reads them and paints the box. Strings are
        //  swapped atomically (single reference assignment), so a torn read
        //  during a frame gives stale-but-consistent text.
        // ============================================================
        private void DrawPnlDisplay ()
        {
            BuildDashboardSnapshot ();
            RequestHudRepaint ();
        }

        private void BuildDashboardSnapshot ()
        {
            try
            {
                int displayTime = ToTime (Time[0]);

                bool timeFilterEnabled = EnableTF1 || EnableTF2 || EnableTF3 || EnableSkipTimeWindow;
                bool inSession = !timeFilterEnabled || CheckTradingTimeframes (displayTime);
                _hudSessionOutside = timeFilterEnabled && !inSession;
                _hudSession = timeFilterEnabled
                    ? (inSession ? "IN SESSION" : "OUT OF SESSION")
                    : "24H (filter off)";

                _hudTitle   = Name ?? "GodZilla";
                _hudVersion = StrategyVersion ?? "";

                _hudArm = _strategyEnabled
                    ? "ENABLED  L:" + (_armLong ? "ON " : "OFF") + "  S:" + (_armShort ? "ON " : "OFF")
                    : "DISABLED";

                string targetStr = UseDailyProfitTarget ? "+$" + DailyProfitTarget.ToString ("F0") : "off";
                string lossStr   = UseDailyLossLimit    ? "-$" + DailyLossLimit.ToString ("F0")   : "off";
                _hudTargets = "Target " + targetStr + "   MaxLoss " + lossStr;

                double openPnl = UseUnrealizedPnl ? dailyUnrealizedPnL : 0.0;
                double sumPnl  = dailyRealizedPnL + openPnl;
                _hudPnlPositive = sumPnl >= 0;

                _hudPnl = string.Format ("Total {0,10}   Closed {1,10}",
                    FormatMoney (totalRunningPnL),
                    FormatMoney (dailyRealizedPnL));
                _hudPnlOpen = UseUnrealizedPnl
                    ? "Open  " + FormatMoney (openPnl).PadLeft (10)
                    : "";

                _hudKillHit = dailyLimitHit;
                _hudKillProfit = dailyLimitHit
                    && !string.IsNullOrEmpty (dailyPnlStatusMessage)
                    && dailyPnlStatusMessage.IndexOf ("PROFIT", StringComparison.OrdinalIgnoreCase) >= 0;
                _hudKillBanner = dailyLimitHit ? (dailyPnlStatusMessage ?? "") : "";

                MarketPosition mp = MarketPosition.Flat;
                try { mp = Position != null ? Position.MarketPosition : MarketPosition.Flat; } catch { }

                if (dailyLimitHit)
                    _hudStatus = _hudKillProfit ? "PROFIT TARGET HIT — flat" : "LOSS LIMIT HIT — flat";
                else if (mp == MarketPosition.Flat)
                    _hudStatus = "IDLE — waiting for signal";
                else
                    _hudStatus = "IN POSITION " + mp.ToString ().ToUpper ();

                _hudShowNews = EnableNewsFilter;
                _hudNews = EnableNewsFilter ? BuildNewsFilterDisplayLine () : "";
                try { _hudNewsBlocked = IsNewsTradingBlocked (); } catch { _hudNewsBlocked = false; }

                _hudLastTrade = string.IsNullOrEmpty (lastTradeClosedSummary) ? "Last: —" : lastTradeClosedSummary;

                _hudShowSignals = EnableSignalTracking;
                if (EnableSignalTracking)
                {
                    var lines = BuildSignalTrackingDisplayLines ();
                    // Atomic swap (assign reference) — UI thread sees old or new list cleanly.
                    _hudSignalLines = lines ?? new List<string> ();
                }
            }
            catch (Exception ex)
            {
                if (_hudErrors < 3)
                {
                    _hudErrors++;
                    if (EnableDebug) Print ("[" + Name + "] HUD snapshot error: " + ex.Message);
                }
            }
        }

        private static string FormatMoney (double v)
        {
            string sign = v >= 0 ? "+$" : "-$";
            return sign + Math.Abs (v).ToString ("F2");
        }

        private void RequestHudRepaint ()
        {
            // Stability-mode short-circuit: if the user disabled the dashboard,
            // there's nothing to paint, so skip the dispatcher InvalidateVisual()
            // entirely. Removes its contribution to dispatcher pressure during
            // fast-forward playback (per AGENTS.md gotcha #17).
            if (!ShowDashboard || DashboardPosition == HudCorner.Hidden) return;

            ChartControl cc = ChartControl;
            if (cc == null) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastHudInvalidateUtc).TotalMilliseconds < HUD_MIN_INVALIDATE_MS)
                return;
            _lastHudInvalidateUtc = now;
            try { cc.InvalidateVisual (); } catch { }
        }

        // OnRenderTargetChanged: NT8 calls this when chart resizes / device lost.
        // Per AGENTS.md lifecycle defense + feedback_nt8_lifecycle_defense.md:
        // try/catch wrap, same-target-skip *gated by _dxInitialized*, eager re-init
        // (never lazy in OnRender) so brushes are never null between dispose and
        // the next frame.
        public override void OnRenderTargetChanged ()
        {
            try { base.OnRenderTargetChanged (); } catch { }

            try
            {
                // Same-target-skip: if NT8 fires this with the same RT we're already
                // bound to AND brushes are valid, do nothing. Disposing/recreating
                // brushes against the same RT can hard-kill the dashboard during
                // playback transitions (historical→realtime). Gating on
                // _dxInitialized covers the case where a prior init failed mid-flight.
                if (RenderTarget != null
                    && _dxInitialized
                    && object.ReferenceEquals (RenderTarget, _lastSeenRenderTarget))
                    return;

                DisposeSharpDxResources ();   // sets _dxInitialized = false

                if (RenderTarget == null) return;
                CreateSharpDxResources ();    // sets _dxInitialized = true on success
                _lastSeenRenderTarget = RenderTarget;
            }
            catch (Exception ex)
            {
                if (_hudErrors < 3)
                {
                    _hudErrors++;
                    if (EnableDebug) Print ("[" + Name + "] HUD RTC err: " + ex.Message);
                }
            }
        }

        // Per-DashboardSize layout table. Body font is monospace (column alignment),
        // title font is proportional (Segoe UI Bold) — but we still budget BOX_W off
        // the body font since that's what drives the longest line.
        // Char width approx (Consolas): 10pt≈6, 11pt≈6.6, 12pt≈7.2, 14pt≈8.4, 17pt≈10.2
        // The widest content line is ~36 chars: "Total +$X,XXX.XX   Closed +$X,XXX.XX"
        private float HudBodyFontSize  () { switch (DashboardSize) { case GodZillaHudSize.Tiny: return 10f; case GodZillaHudSize.Small: return 11f; case GodZillaHudSize.Large: return 14f; case GodZillaHudSize.Huge: return 17f; default: return 12f; } }
        private float HudTitleFontSize () { switch (DashboardSize) { case GodZillaHudSize.Tiny: return 12f; case GodZillaHudSize.Small: return 13f; case GodZillaHudSize.Large: return 17f; case GodZillaHudSize.Huge: return 21f; default: return 14f; } }
        private float HudBoxWidth      () { switch (DashboardSize) { case GodZillaHudSize.Tiny: return 250f; case GodZillaHudSize.Small: return 280f; case GodZillaHudSize.Large: return 380f; case GodZillaHudSize.Huge: return 460f; default: return 320f; } }
        private float HudRowHeight     () { switch (DashboardSize) { case GodZillaHudSize.Tiny: return 13f; case GodZillaHudSize.Small: return 14f; case GodZillaHudSize.Large: return 19f; case GodZillaHudSize.Huge: return 22f; default: return 16f; } }
        private float HudTitleHeight   () { switch (DashboardSize) { case GodZillaHudSize.Tiny: return 18f; case GodZillaHudSize.Small: return 20f; case GodZillaHudSize.Large: return 25f; case GodZillaHudSize.Huge: return 28f; default: return 22f; } }

        // Builds (or rebuilds) just the TextFormats. Cheap — fonts are
        // recreated on size change without touching the brush set.
        private void EnsureDashboardFonts ()
        {
            if (_lastSizeApplied == DashboardSize && _dashFormat != null && _dashTitleFormat != null)
                return;

            try { if (_dashFormat      != null) { _dashFormat.Dispose ();      _dashFormat      = null; } } catch { _dashFormat = null; }
            try { if (_dashTitleFormat != null) { _dashTitleFormat.Dispose (); _dashTitleFormat = null; } } catch { _dashTitleFormat = null; }

            var dwf = NinjaTrader.Core.Globals.DirectWriteFactory;

            _dashFormat = new SharpDX.DirectWrite.TextFormat (
                dwf, "Consolas", FontWeight.Normal, FontStyle.Normal, HudBodyFontSize ());
            _dashFormat.WordWrapping       = SharpDX.DirectWrite.WordWrapping.NoWrap;
            _dashFormat.TextAlignment      = SharpDX.DirectWrite.TextAlignment.Leading;
            _dashFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;

            _dashTitleFormat = new SharpDX.DirectWrite.TextFormat (
                dwf, "Segoe UI", FontWeight.Bold, FontStyle.Normal, HudTitleFontSize ());
            _dashTitleFormat.WordWrapping       = SharpDX.DirectWrite.WordWrapping.NoWrap;
            _dashTitleFormat.TextAlignment      = SharpDX.DirectWrite.TextAlignment.Leading;
            _dashTitleFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;

            _lastSizeApplied = DashboardSize;
        }

        private void CreateSharpDxResources ()
        {
            if (RenderTarget == null) return;

            EnsureDashboardFonts ();

            _bTextWhite  = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (0.95f, 0.95f, 0.95f, 1f));
            _bTextDim    = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (0.65f, 0.65f, 0.70f, 1f));
            _bTextGreen  = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (0.20f, 1.00f, 0.30f, 1f));
            _bTextRed    = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (1.00f, 0.30f, 0.30f, 1f));
            _bTextYellow = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (1.00f, 0.85f, 0.20f, 1f));
            _bTextOrange = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (1.00f, 0.55f, 0.10f, 1f));
            _bTextCyan   = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (0.30f, 0.85f, 1.00f, 1f));
            _bBackground = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (0.05f, 0.06f, 0.10f, 0.86f));
            _bBorder     = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (0.35f, 0.40f, 0.55f, 1.00f));
            _bBorderHot  = new SharpDX.Direct2D1.SolidColorBrush (RenderTarget, new Color4 (1.00f, 0.55f, 0.10f, 1.00f));

            _dxInitialized = true;
        }

        private void DisposeSharpDxResources ()
        {
            _dxInitialized = false;
            try { if (_dashFormat       != null) { _dashFormat.Dispose ();       _dashFormat       = null; } } catch { _dashFormat = null; }
            try { if (_dashTitleFormat  != null) { _dashTitleFormat.Dispose ();  _dashTitleFormat  = null; } } catch { _dashTitleFormat = null; }
            try { if (_bTextWhite       != null) { _bTextWhite.Dispose ();       _bTextWhite       = null; } } catch { _bTextWhite = null; }
            try { if (_bTextDim         != null) { _bTextDim.Dispose ();         _bTextDim         = null; } } catch { _bTextDim = null; }
            try { if (_bTextGreen       != null) { _bTextGreen.Dispose ();       _bTextGreen       = null; } } catch { _bTextGreen = null; }
            try { if (_bTextRed         != null) { _bTextRed.Dispose ();         _bTextRed         = null; } } catch { _bTextRed = null; }
            try { if (_bTextYellow      != null) { _bTextYellow.Dispose ();      _bTextYellow      = null; } } catch { _bTextYellow = null; }
            try { if (_bTextOrange      != null) { _bTextOrange.Dispose ();      _bTextOrange      = null; } } catch { _bTextOrange = null; }
            try { if (_bTextCyan        != null) { _bTextCyan.Dispose ();        _bTextCyan        = null; } } catch { _bTextCyan = null; }
            try { if (_bBackground      != null) { _bBackground.Dispose ();      _bBackground      = null; } } catch { _bBackground = null; }
            try { if (_bBorder          != null) { _bBorder.Dispose ();          _bBorder          = null; } } catch { _bBorder = null; }
            try { if (_bBorderHot       != null) { _bBorderHot.Dispose ();       _bBorderHot       = null; } } catch { _bBorderHot = null; }
        }

        // OnRender — UI thread. NEVER touch bar series, Position, ATM APIs here
        // (they may throw on the UI thread). Read only from snapshot fields.
        protected override void OnRender (ChartControl chartControl, ChartScale chartScale)
        {
            try { base.OnRender (chartControl, chartScale); } catch { }

            if (RenderTarget == null) return;

            // User-disabled the HUD entirely — skip all SharpDX work. Stability mode.
            if (!ShowDashboard || DashboardPosition == HudCorner.Hidden) return;

            // Eager re-init: OnRenderTargetChanged may not have fired yet on
            // some NT8 builds (notably during playback restart), so brushes
            // can be null on the first frame. Build them now if missing.
            if (_dashFormat == null || _bTextWhite == null)
            {
                try { CreateSharpDxResources (); } catch (Exception ex) { if (_hudErrors < 3) { _hudErrors++; if (EnableDebug) Print ("[" + Name + "] HUD lazy-init err: " + ex.Message); } }
                if (_dashFormat == null) return;
            }

            try
            {
                // Snapshot copies (single ref read each — cannot be mid-mutated).
                string title       = _hudTitle;
                string version     = _hudVersion;
                string arm         = _hudArm;
                string sess        = _hudSession;
                string news        = _hudNews;
                string pnl         = _hudPnl;
                string pnlOpen     = _hudPnlOpen;
                string targets     = _hudTargets;
                string status      = _hudStatus;
                string last        = _hudLastTrade;
                string killBanner  = _hudKillBanner;
                bool   pnlPositive = _hudPnlPositive;
                bool   sessOutside = _hudSessionOutside;
                bool   killHit     = _hudKillHit;
                bool   killProfit  = _hudKillProfit;
                bool   newsBlocked = _hudNewsBlocked;
                bool   showNews    = _hudShowNews;
                bool   showSignals = _hudShowSignals;
                List<string> sigLines = _hudSignalLines;  // ref snapshot

                // Make sure the fonts match the current DashboardSize. Cheap when
                // size hasn't changed (single bool compare); rebuilds the two text
                // formats when the user picks a new preset at runtime.
                EnsureDashboardFonts ();

                // Per-size layout table — drives both font sizes (above) and the box
                // dimensions (below). Tightened from the old fixed 460px so the box
                // hugs the actual content width at each preset.
                const float PAD     = 8f;       // inner padding (was 10) — saves ~4px each side
                const float SEP_H   = 4f;
                float ROW_H   = HudRowHeight   ();
                float TITLE_H = HudTitleHeight ();
                float BOX_W   = HudBoxWidth    ();
                const float MARGIN_X = 18f;     // horizontal margin from chart edge
                const float MARGIN_Y = 35f;     // vertical margin from chart edge
                                                // (35f from bottom dodges the time axis;
                                                //  same value at top stays under axis labels)

                // Count rows
                int rows = 0;
                rows++; // title
                rows++; // separator
                rows++; // arm
                rows++; // session
                if (showNews && !string.IsNullOrEmpty (news)) rows++;
                rows++; // pnl
                if (!string.IsNullOrEmpty (pnlOpen)) rows++;
                rows++; // targets
                rows++; // status
                rows++; // last trade
                if (showSignals && sigLines != null && sigLines.Count > 0) rows += sigLines.Count;

                float boxH = PAD * 2f + TITLE_H + SEP_H + ROW_H * (rows - 1) + 4f;

                Size2F rtSize = RenderTarget.Size;
                float bx, by;
                switch (DashboardPosition)
                {
                    case HudCorner.TopLeft:
                        bx = MARGIN_X;
                        by = MARGIN_Y;
                        break;
                    case HudCorner.TopRight:
                        bx = rtSize.Width  - BOX_W - MARGIN_X;
                        by = MARGIN_Y;
                        break;
                    case HudCorner.BottomLeft:
                        bx = MARGIN_X;
                        by = rtSize.Height - boxH  - MARGIN_Y;
                        break;
                    case HudCorner.Center:
                        bx = (rtSize.Width  - BOX_W) * 0.5f;
                        by = (rtSize.Height - boxH)  * 0.5f;
                        break;
                    case HudCorner.BottomRight:
                    default:
                        bx = rtSize.Width  - BOX_W - MARGIN_X;
                        by = rtSize.Height - boxH  - MARGIN_Y;
                        break;
                }
                // Clamp to chart bounds — never let the box leak off-screen on
                // very narrow/short charts (workspace tab tiles, etc.).
                if (bx < 4f) bx = 4f;
                if (by < 4f) by = 4f;
                if (bx + BOX_W > rtSize.Width  - 4f) bx = rtSize.Width  - BOX_W - 4f;
                if (by + boxH  > rtSize.Height - 4f) by = rtSize.Height - boxH  - 4f;

                RectangleF box = new RectangleF (bx, by, BOX_W, boxH);
                RenderTarget.FillRectangle (box, _bBackground);
                RenderTarget.DrawRectangle (box, killHit ? _bBorderHot : _bBorder, killHit ? 2f : 1f);

                float x = bx + PAD;
                float y = by + PAD;
                float w = BOX_W - PAD * 2f;

                // Title row
                string titleLine = "🐲 " + title + (string.IsNullOrEmpty (version) ? "" : "  v" + version);
                DrawHudLine (titleLine, x, y, w, TITLE_H, _bTextCyan, _dashTitleFormat);
                y += TITLE_H;

                // Thin separator
                RenderTarget.DrawLine (
                    new Vector2 (x, y + 1f),
                    new Vector2 (x + w, y + 1f),
                    _bBorder, 1f);
                y += SEP_H;

                // ARM line — green if enabled, dim if not
                DrawHudLine (arm, x, y, w, ROW_H, _strategyEnabled ? _bTextGreen : _bTextRed, _dashFormat);
                y += ROW_H;

                // Session
                DrawHudLine ("Session: " + sess, x, y, w, ROW_H,
                    sessOutside ? _bTextOrange : _bTextGreen, _dashFormat);
                y += ROW_H;

                // News (if enabled)
                if (showNews && !string.IsNullOrEmpty (news))
                {
                    DrawHudLine (news, x, y, w, ROW_H,
                        newsBlocked ? _bTextRed : _bTextDim, _dashFormat);
                    y += ROW_H;
                }

                // PnL totals
                DrawHudLine (pnl, x, y, w, ROW_H,
                    pnlPositive ? _bTextGreen : _bTextRed, _dashFormat);
                y += ROW_H;

                if (!string.IsNullOrEmpty (pnlOpen))
                {
                    DrawHudLine (pnlOpen, x, y, w, ROW_H, _bTextWhite, _dashFormat);
                    y += ROW_H;
                }

                // Targets
                DrawHudLine (targets, x, y, w, ROW_H, _bTextDim, _dashFormat);
                y += ROW_H;

                // Status
                SharpDX.Direct2D1.SolidColorBrush statusBrush;
                if (killHit)            statusBrush = killProfit ? _bTextGreen : _bTextRed;
                else if (status != null && status.StartsWith ("IN POSITION", StringComparison.Ordinal)) statusBrush = _bTextCyan;
                else                    statusBrush = _bTextYellow;
                DrawHudLine (status, x, y, w, ROW_H, statusBrush, _dashFormat);
                y += ROW_H;

                // Last trade
                DrawHudLine (last, x, y, w, ROW_H, _bTextDim, _dashFormat);
                y += ROW_H;

                // Signal-tracking lines
                if (showSignals && sigLines != null)
                {
                    for (int i = 0; i < sigLines.Count; i++)
                    {
                        DrawHudLine (sigLines[i], x, y, w, ROW_H, _bTextWhite, _dashFormat);
                        y += ROW_H;
                    }
                }

                // Kill-switch banner overlay (centered atop chart)
                if (killHit && !string.IsNullOrEmpty (killBanner))
                {
                    var kb = new RectangleF (rtSize.Width * 0.18f, rtSize.Height * 0.42f,
                                              rtSize.Width * 0.64f, 60f);
                    RenderTarget.FillRectangle (kb, _bBackground);
                    RenderTarget.DrawRectangle (kb, killProfit ? _bTextGreen : _bTextRed, 2f);
                    RenderTarget.DrawText (killBanner, _dashTitleFormat,
                        new RectangleF (kb.X + 12f, kb.Y + 18f, kb.Width - 24f, kb.Height - 18f),
                        killProfit ? _bTextGreen : _bTextRed,
                        SharpDX.Direct2D1.DrawTextOptions.Clip,
                        SharpDX.Direct2D1.MeasuringMode.Natural);
                }

                if (!_hudFirstRenderLogged && EnableDebug)
                {
                    _hudFirstRenderLogged = true;
                    Print ("[" + Name + "] HUD: first frame rendered ("
                        + rtSize.Width.ToString ("F0") + "x" + rtSize.Height.ToString ("F0") + ")");
                }
            }
            catch (Exception ex)
            {
                if (_hudErrors < 3)
                {
                    _hudErrors++;
                    if (EnableDebug) Print ("[" + Name + "] HUD render err: " + ex.Message);
                }
            }
        }

        private void DrawHudLine (string text, float x, float y, float w, float h,
                                   SharpDX.Direct2D1.SolidColorBrush brush,
                                   SharpDX.DirectWrite.TextFormat format)
        {
            RectangleF rect = new RectangleF (x, y, w, h + 4f);
            RenderTarget.DrawText (text ?? "", format, rect, brush,
                SharpDX.Direct2D1.DrawTextOptions.Clip,
                SharpDX.Direct2D1.MeasuringMode.Natural);
        }

        // Most-recent non-zero signal value within the last `windowBars` bars (inclusive).
        // Used by rolling group entry triggers — lets indicators with different
        // firing cadences "co-vote" within a tolerance window.
        private double LatestSignalInWindow (NinjaTrader.NinjaScript.Series<double> series, int windowBars)
        {
            if (series == null) return 0;
            int n = Math.Min (windowBars, CurrentBar);
            for (int i = 0; i <= n; i++)
            {
                double v;
                try { v = series[i]; }
                catch { return 0; }
                if (v > 0) return 1;
                if (v < 0) return -1;
            }
            return 0;
        }

        private void SetActiveTradeSignalSources (bool usePanaZillia, bool useKingZilla, bool useKingPana, bool useSumo, bool useSjb, MarketPosition direction)
        {
            activeTradeUsesPanaZillia = usePanaZillia;
            activeTradeUsesKingZilla = useKingZilla;
            activeTradeUsesKingPana = useKingPana;
            activeTradeUsesSumo = useSumo;
            activeTradeUsesSjb = useSjb;
            activeTradeDirection = direction;
        }

        private void ClearActiveTradeSignalSources ()
        {
            activeTradeUsesPanaZillia = false;
            activeTradeUsesKingZilla = false;
            activeTradeUsesKingPana = false;
            activeTradeUsesSumo = false;
            activeTradeUsesSjb = false;
            activeTradeDirection = MarketPosition.Flat;
        }

        private void UpdateSignalTrackingOnTradeClose (double tradePnl)
        {
            if (!EnableSignalTracking)
                return;

            bool isWinner = tradePnl > 0;
            bool isLoser = tradePnl < 0;

            if (activeTradeUsesPanaZillia)
                IncrementSignalStats (panaZilliaStats, activeTradeDirection, isWinner, isLoser);

            if (activeTradeUsesKingZilla)
                IncrementSignalStats (kingZillaStats, activeTradeDirection, isWinner, isLoser);

            if (activeTradeUsesKingPana)
                IncrementSignalStats (kingPanaStats, activeTradeDirection, isWinner, isLoser);

            if (activeTradeUsesSumo)
                IncrementSignalStats (sumoStats, activeTradeDirection, isWinner, isLoser);

            if (activeTradeUsesSjb)
                IncrementSignalStats (sjbStats, activeTradeDirection, isWinner, isLoser);
        }

        private void IncrementSignalStats (SignalTradeStats stats, MarketPosition direction, bool isWinner, bool isLoser)
        {
            stats.TotalTrades++;

            if (direction == MarketPosition.Long)
                stats.LongTrades++;
            else if (direction == MarketPosition.Short)
                stats.ShortTrades++;

            if (isWinner)
                stats.Winners++;
            else if (isLoser)
                stats.Losers++;
        }

        private void UpdateLastTradeClosedSummary (double tradePnl)
        {
            if (!EnableSignalTracking)
                return;

            string dir = activeTradeDirection == MarketPosition.Long ? "Long" : activeTradeDirection == MarketPosition.Short ? "Short" : "Flat";
            string sig = BuildActiveSignalAbbreviationList();
            lastTradeClosedSummary = $"Last: {tradePnl:C} | {dir} | {sig}";
        }

        private void PrintSignalTrackingOnTradeClose (double tradePnl)
        {
            if (!EnableSignalTracking)
                return;

            string tradeSignalText = BuildActiveSignalListForPrint();
            string statsText = BuildSignalTrackingDisplayText();

            // Removed the invalid .Replace("", Environment.NewLine) call
            if (EnableDebug)
                Print ($"Trade Closed | Last ATM P/L: {tradePnl:C} | Direction: {activeTradeDirection} | Signals: {tradeSignalText}{statsText}");
        }

        private string BuildActiveSignalListForPrint ()
        {
            List<string> activeSignals = new List<string>();

            if (activeTradeUsesPanaZillia)
                activeSignals.Add ("PanaZillia");
            if (activeTradeUsesKingZilla)
                activeSignals.Add ("KingZilla");
            if (activeTradeUsesKingPana)
                activeSignals.Add ("KingPana");
            if (activeTradeUsesSumo)
                activeSignals.Add ("SumoPullback");
            if (activeTradeUsesSjb)
                activeSignals.Add ("SuperJumpBoost");

            return activeSignals.Count > 0 ? string.Join (", ", activeSignals) : "None";
        }

        private string BuildActiveSignalAbbreviationList ()
        {
            List<string> activeSignals = new List<string>();

            if (activeTradeUsesPanaZillia)
                activeSignals.Add ("PZ");
            if (activeTradeUsesKingZilla)
                activeSignals.Add ("KZ");
            if (activeTradeUsesKingPana)
                activeSignals.Add ("KP");
            if (activeTradeUsesSumo)
                activeSignals.Add ("SUMO");
            if (activeTradeUsesSjb)
                activeSignals.Add ("SJB");

            return activeSignals.Count > 0 ? string.Join ("+", activeSignals) : "None";
        }

        private string BuildSignalTrackingDisplayText ()
        {
            if (!EnableSignalTracking)
                return string.Empty;

            // Join the display lines directly with a new line instead of an empty string
            return Environment.NewLine + string.Join (Environment.NewLine, BuildSignalTrackingDisplayLines ());
        }

        private List<string> BuildSignalTrackingDisplayLines ()
        {
            List<string> enabledSignals = new List<string>();
            List<string> lines = new List<string>();

            if (UsePanaZilliaSignals)
            {
                enabledSignals.Add ("PZ");
                lines.Add ($"PZ T:{panaZilliaStats.TotalTrades} Lg:{panaZilliaStats.LongTrades} Sh:{panaZilliaStats.ShortTrades} W:{panaZilliaStats.Winners} L:{panaZilliaStats.Losers}");
            }

            if (UseKingZillaSignals)
            {
                enabledSignals.Add ("KZ");
                lines.Add ($"KZ T:{kingZillaStats.TotalTrades} Lg:{kingZillaStats.LongTrades} Sh:{kingZillaStats.ShortTrades} W:{kingZillaStats.Winners} L:{kingZillaStats.Losers}");
            }

            if (UseKingPanaSignals)
            {
                enabledSignals.Add ("KP");
                lines.Add ($"KP T:{kingPanaStats.TotalTrades} Lg:{kingPanaStats.LongTrades} Sh:{kingPanaStats.ShortTrades} W:{kingPanaStats.Winners} L:{kingPanaStats.Losers}");
            }

            if (UseSumoPullbackSignals)
            {
                enabledSignals.Add ("SUMO");
                lines.Add ($"SUMO T:{sumoStats.TotalTrades} Lg:{sumoStats.LongTrades} Sh:{sumoStats.ShortTrades} W:{sumoStats.Winners} L:{sumoStats.Losers}");
            }

            if (UseSuperJumpBoostSignals)
            {
                enabledSignals.Add ("SJB");
                lines.Add ($"SJB T:{sjbStats.TotalTrades} Lg:{sjbStats.LongTrades} Sh:{sjbStats.ShortTrades} W:{sjbStats.Winners} L:{sjbStats.Losers}");
            }

            lines.Insert (0, enabledSignals.Count > 0 ? "Enabled Signals: " + string.Join (", ", enabledSignals) : "Enabled Signals: None");
            return lines;
        }

        private bool CheckTradingTimeframes (int currentTime)
        {
            bool anyTradingWindowEnabled = EnableTF1 || EnableTF2 || EnableTF3;

            bool tf1 = EnableTF1 && IsTimeInWindow (currentTime, ToTime (StartTime1), ToTime (EndTime1));
            bool tf2 = EnableTF2 && IsTimeInWindow (currentTime, ToTime (StartTime2), ToTime (EndTime2));
            bool tf3 = EnableTF3 && IsTimeInWindow (currentTime, ToTime (StartTime3), ToTime (EndTime3));

            bool allowedByTradingWindows = !anyTradingWindowEnabled || tf1 || tf2 || tf3;

            if (!allowedByTradingWindows)
                return false;

            if (EnableSkipTimeWindow && IsTimeInWindow (currentTime, ToTime (SkipStartTime), ToTime (SkipEndTime)))
                return false;

            return true;
        }

        private void CheckFlattenTimeframes (int currentTime)
        {
            // If no TF filters are enabled, do not do any TF-based flattening
            if (!EnableTF1 && !EnableTF2 && !EnableTF3)
                return;

            if (CurrentBar < 1)
                return;

            int previousTime = ToTime (Time[1]);

            bool flatten1 = EnableTF1 && FlattenTF1
                && IsTimeInWindow (previousTime, ToTime (StartTime1), ToTime (EndTime1))
                && !IsTimeInWindow (currentTime, ToTime (StartTime1), ToTime (EndTime1));

            bool flatten2 = EnableTF2 && FlattenTF2
                && IsTimeInWindow (previousTime, ToTime (StartTime2), ToTime (EndTime2))
                && !IsTimeInWindow (currentTime, ToTime (StartTime2), ToTime (EndTime2));

            bool flatten3 = EnableTF3 && FlattenTF3
                && IsTimeInWindow (previousTime, ToTime (StartTime3), ToTime (EndTime3))
                && !IsTimeInWindow (currentTime, ToTime (StartTime3), ToTime (EndTime3));

            if (flatten1 || flatten2 || flatten3)
                FlattenAll ();
        }

        private bool IsTimeInWindow (int currentTime, int startTime, int endTime)
        {
            // Treat equal start/end as always on
            if (startTime == endTime)
                return true;

            // Normal same-day window
            if (startTime < endTime)
                return currentTime >= startTime && currentTime < endTime;

            // Overnight window, ex: 180000 -> 040000
            return currentTime >= startTime || currentTime < endTime;
        }

        private void FlattenAll ()
        {
            FlattenEverything ("FlattenAll requested");
        }

        private void FlattenEverything (string reason)
        {
            if (State != State.Realtime)
                return;

            // Always print the reason — this is a high-signal, low-frequency event
            // (only fires on actual flattens). Diagnosing "why did my position close
            // immediately after opening?" requires knowing which trigger fired.
            Print ($"[{Name}] FlattenEverything ({Time[0]:HH:mm:ss}): {reason}  "
                + $"strategyPos={Position.MarketPosition} qty={Position.Quantity}  "
                + $"atmId={(string.IsNullOrEmpty(atmStrategyId) ? "<none>" : atmStrategyId)}");

            // 1) Close ATM if we have one tracked
            if (!string.IsNullOrEmpty (atmStrategyId))
            {
                try
                {
                    AtmStrategyClose (atmStrategyId);
                }
                catch { /* swallow */ }
            }

            // 2) Belt-and-suspenders: if account still shows a position, hit it directly.
            //    With IsAdoptAccountPositionAware = true, ExitLong/ExitShort will flatten
            //    the adopted position via a market order.
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong ("NakedFlat", "");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort ("NakedFlat", "");

            // 3) Cancel any orphaned working orders on this instrument
            if (Account != null)
            {
                List<Order> toCancel = new List<Order>();
                lock (Account.Orders)
                {
                    foreach (Order o in Account.Orders)
                    {
                        if (o.Instrument != Instrument)
                            continue;
                        if (o.OrderState == OrderState.Working
                            || o.OrderState == OrderState.Accepted
                            || o.OrderState == OrderState.Submitted)
                            toCancel.Add (o);
                    }
                }
                foreach (Order o in toCancel)
                {
                    try
                    {
                        Account.Cancel (new[] { o });
                    }
                    catch { /* swallow */ }
                }
            }
        }

        private void CheckForNakedPositions (DateTime tickTime)
        {
            // Throttle is now handled by the caller via wall-clock UTC
            // (lastNakedCheckUtc). tickTime is kept only for diagnostic prints.
            if (State != State.Realtime)
                return;

            // Nothing to check if we're flat
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            bool hasActiveAtm = !string.IsNullOrEmpty(atmStrategyId)
                && GetAtmStrategyMarketPosition(atmStrategyId) != Cbi.MarketPosition.Flat;

            bool hasProtectiveOrders = HasWorkingProtectiveOrders();

            if (!hasActiveAtm || !hasProtectiveOrders)
            {
                if (EnableDebug)
                    Print ($"[{Name}] NAKED POSITION DETECTED at {tickTime:HH:mm:ss} | "
                    + $"Position={Position.MarketPosition} {Position.Quantity} | "
                    + $"HasActiveAtm={hasActiveAtm} HasProtectiveOrders={hasProtectiveOrders}. Flattening.");

                FlattenEverything ("Naked position watchdog");
            }
        }

        private bool HasWorkingProtectiveOrders ()
        {
            if (Account == null)
                return false;

            lock (Account.Orders)
            {
                foreach (Order o in Account.Orders)
                {
                    if (o.Instrument != Instrument)
                        continue;

                    if (o.OrderState != OrderState.Working
                        && o.OrderState != OrderState.Accepted
                        && o.OrderState != OrderState.Submitted)
                        continue;

                    // Exit-side stop or target relative to current position
                    bool isExitSide =
                        (Position.MarketPosition == MarketPosition.Long && (o.OrderAction == OrderAction.Sell || o.OrderAction == OrderAction.SellShort))
                     || (Position.MarketPosition == MarketPosition.Short && (o.OrderAction == OrderAction.Buy || o.OrderAction == OrderAction.BuyToCover));

                    if (!isExitSide)
                        continue;

                    if (o.OrderType == OrderType.StopMarket
                        || o.OrderType == OrderType.StopLimit
                        || o.OrderType == OrderType.Limit)
                        return true;
                }
            }

            return false;
        }

        #region RBro Button Panel

        private void CreateRBroControlPanel ()
        {
            if (ChartControl == null || _uiInitialized)
                return;
            // User opted-out of the on-chart button panel.
            if (!ShowControlPanel || ControlPanelPosition == HudCorner.Hidden)
                return;

            // Map the corner setting to WPF alignments + per-corner Margin so the
            // panel snaps to the requested chart edge with a 10px breathing margin.
            HorizontalAlignment hAlign;
            VerticalAlignment   vAlign;
            Thickness panelMargin;
            switch (ControlPanelPosition)
            {
                case HudCorner.TopRight:
                    hAlign = HorizontalAlignment.Right; vAlign = VerticalAlignment.Top;
                    panelMargin = new Thickness (0, 10, 10, 0);
                    break;
                case HudCorner.BottomLeft:
                    hAlign = HorizontalAlignment.Left;  vAlign = VerticalAlignment.Bottom;
                    panelMargin = new Thickness (10, 0, 0, 10);
                    break;
                case HudCorner.BottomRight:
                    hAlign = HorizontalAlignment.Right; vAlign = VerticalAlignment.Bottom;
                    panelMargin = new Thickness (0, 0, 10, 10);
                    break;
                case HudCorner.Center:
                    hAlign = HorizontalAlignment.Center; vAlign = VerticalAlignment.Center;
                    panelMargin = new Thickness (0);
                    break;
                case HudCorner.TopLeft:
                default:
                    hAlign = HorizontalAlignment.Left;  vAlign = VerticalAlignment.Top;
                    panelMargin = new Thickness (10, 10, 0, 0);
                    break;
            }

            ChartControl.Dispatcher.InvokeAsync (() =>
            {
                try
                {
                    if (_uiInitialized)
                        return;

                    _controlPanel = new Border
                    {
                        Background = new SolidColorBrush (Color.FromArgb (220, 20, 20, 35)),
                        BorderBrush = Brushes.DodgerBlue,
                        BorderThickness = new Thickness (2),
                        CornerRadius = new CornerRadius (5),
                        Padding = new Thickness (10),
                        Margin = panelMargin,
                        HorizontalAlignment = hAlign,
                        VerticalAlignment = vAlign
                    };

                    StackPanel main = new StackPanel { Orientation = Orientation.Vertical };

                    main.Children.Add (new TextBlock
                    {
                        Text = "🐲 GodZilla 🐲",
                        Foreground = Brushes.Cyan,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness (0, 0, 0, 0)
                    });

                    main.Children.Add (new TextBlock
                    {
                        Text = "Instrument -- " + ((Instrument != null && Instrument.MasterInstrument != null) ? Instrument.MasterInstrument.Name : "Unknown"),
                        Foreground = Brushes.DeepSkyBlue,
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness (0, 0, 0, 4)
                    });

                    _statusLabel = new Label
                    {
                        Content = "Initializing...",
                        Foreground = Brushes.Yellow,
                        FontSize = 11,
                        Padding = new Thickness (0),
                        Margin = new Thickness (0, 0, 0, 4)
                    };
                    main.Children.Add (_statusLabel);

                    StackPanel btnRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness (0, 0, 0, 0)
                    };

                    _armLongBtn = new Button
                    {
                        Content = "ARM LONG",
                        Width = 150,                                  // fits "ARMED SHORT" (11 chars Bold 11pt) with WPF template padding
                        Height = 30,
                        Margin = new Thickness (2),
                        Background = Brushes.DarkGreen,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        HorizontalContentAlignment = HorizontalAlignment.Center
                    };
                    _armLongBtn.Click += ArmLongBtn_Click;
                    btnRow.Children.Add (_armLongBtn);

                    _armShortBtn = new Button
                    {
                        Content = "ARM SHORT",
                        Width = 150,
                        Height = 30,
                        Margin = new Thickness (2),
                        Background = Brushes.DarkRed,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        HorizontalContentAlignment = HorizontalAlignment.Center
                    };
                    _armShortBtn.Click += ArmShortBtn_Click;
                    btnRow.Children.Add (_armShortBtn);

                    main.Children.Add (btnRow);

                    _autoArmBtn = new Button
                    {
                        Content = "AUTO ARM: OFF",
                        Width = 304,                                  // matches arm-row total (150+150+4 margins)
                        Height = 30,
                        Margin = new Thickness (2, 4, 2, 0),
                        Background = Brushes.DimGray,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        HorizontalContentAlignment = HorizontalAlignment.Center
                    };
                    _autoArmBtn.Click += AutoArmBtn_Click;
                    main.Children.Add (_autoArmBtn);

                    _closeBtn = new Button
                    {
                        Content = "CLOSE ALL",
                        Width = 304,
                        Height = 30,
                        Margin = new Thickness (2, 4, 2, 0),
                        Background = Brushes.Maroon,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        HorizontalContentAlignment = HorizontalAlignment.Center
                    };
                    _closeBtn.Click += CloseBtn_Click;
                    main.Children.Add (_closeBtn);

                    _controlPanel.Child = main;
                    UserControlCollection.Add (_controlPanel);
                    _uiInitialized = true;

                    UpdateRBroButtons ();
                    UpdateRBroStatusUI ();
                }
                catch (Exception ex)
                {
                    if (EnableDebug)
                        Print ($"[{Name}] Button panel error: {ex.Message}");
                }
            });
        }

        private void RemoveRBroControlPanel ()
        {
            if (ChartControl == null || _controlPanel == null)
                return;

            ChartControl.Dispatcher.InvokeAsync (() =>
            {
                try
                {
                    if (_controlPanel != null && UserControlCollection.Contains (_controlPanel))
                        UserControlCollection.Remove (_controlPanel);

                    _controlPanel = null;
                    _uiInitialized = false;
                }
                catch { }
            });
        }

        private void ArmLongBtn_Click (object sender, RoutedEventArgs e)
        {
            _armLong = !_armLong;
            UpdateRBroButtons ();
            UpdateRBroStatusUI ();
        }

        private void ArmShortBtn_Click (object sender, RoutedEventArgs e)
        {
            _armShort = !_armShort;
            UpdateRBroButtons ();
            UpdateRBroStatusUI ();
        }

        private void AutoArmBtn_Click (object sender, RoutedEventArgs e)
        {
            _autoArm = !_autoArm;

            if (_autoArm)
                _armLong = _armShort = true;
            // Note: disabling AutoArm does not force the individual arm toggles off —
            // the user controls those independently.

            UpdateRBroButtons ();
            UpdateRBroStatusUI ();
        }

        private void CloseBtn_Click (object sender, RoutedEventArgs e)
        {
            FlattenAll ();
            UpdateRBroStatusUI ();
        }

        private void UpdateRBroButtons ()
        {
            if (_armLongBtn == null || _armShortBtn == null || _autoArmBtn == null)
                return;

            _armLongBtn.Background = _armLong ? Brushes.LimeGreen : Brushes.DarkGreen;
            _armLongBtn.Content = _armLong ? "ARMED LONG" : "ARM LONG";

            _armShortBtn.Background = _armShort ? Brushes.Red : Brushes.DarkRed;
            _armShortBtn.Content = _armShort ? "ARMED SHORT" : "ARM SHORT";

            _autoArmBtn.Background = _autoArm ? Brushes.DodgerBlue : Brushes.DimGray;
            _autoArmBtn.Content = _autoArm ? "AUTO ARM: ON" : "AUTO ARM: OFF";
        }

        private void UpdateRBroStatusUI ()
        {
            if (_statusLabel == null || ChartControl == null)
                return;

            ChartControl.Dispatcher.InvokeAsync (() =>
            {
                try
                {
                    string pos = Position.MarketPosition.ToString ();
                    bool noAtmSelected = string.IsNullOrWhiteSpace (AtmStrategy);
                    string selectedAtm = noAtmSelected ? "No ATM Selected" : AtmStrategy;

                    string suffix = orderId.Length > 0
                ? " [ORDER: " + selectedAtm + "]"
                : " [ATM: " + selectedAtm + "]";

                    _statusLabel.Content = pos + suffix;

                    if (noAtmSelected)
                        _statusLabel.Foreground = Brushes.Red;
                    else
                        _statusLabel.Foreground =
                            dailyLimitHit ? Brushes.Red :
                            Position.MarketPosition != MarketPosition.Flat ? Brushes.Cyan :
                            (_armLong || _armShort) ? Brushes.LimeGreen : Brushes.Orange;
                }
                catch { }
            });
        }

        #endregion

        #region Custom Property Manipulation
        private void ModifyPNLProperties (PropertyDescriptorCollection col)
        {
            if (!UseDailyProfitTarget)
                col.Remove (col["DailyProfitTarget"]);
            if (!UseDailyLossLimit)
                col.Remove (col["DailyLossLimit"]);
        }
        private void ModifySessionProperties (PropertyDescriptorCollection col)
        {
            if (!EnableTF1)
            {
                col.Remove (col["StartTime1"]);
                col.Remove (col["EndTime1"]);
                col.Remove (col["FlattenTF1"]);
            }
            if (!EnableTF2)
            {
                col.Remove (col["StartTime2"]);
                col.Remove (col["EndTime2"]);
                col.Remove (col["FlattenTF2"]);
            }
            if (!EnableTF3)
            {
                col.Remove (col["StartTime3"]);
                col.Remove (col["EndTime3"]);
                col.Remove (col["FlattenTF3"]);
            }
            if (!EnableSkipTimeWindow)
            {
                col.Remove (col["SkipStartTime"]);
                col.Remove (col["SkipEndTime"]);
            }
        }

        private void ModifyEmaFilterProperties (PropertyDescriptorCollection col)
        {
            if (!UseEmaFilter)
            {
                col.Remove (col["EmaShortPeriod"]);
                col.Remove (col["EmaLongPeriod"]);
            }
        }

        private void ModifyNewsFilterProperties (PropertyDescriptorCollection col)
        {
            if (EnableNewsFilter)
                return;

            string[] toRemove = new[]
            {
                "NewsFlattenAtWarningTime",
                "NewsShowDisplay",
                "NewsDisplayLocation",
                "NewsDisplayXOffsetPixels",
                "NewsDisplayYOffsetPixels",
                "NewsUse24HourTime",
                "NewsShowBackground",
                "NewsShowTimeBackBrush",
                "NewsTimeBackBrush",
                "NewsUSOnlyEvents",
                "NewsTodaysNewsOnly",
                "NewsShowLowPriority",
                "NewsMaxNewsItems",
                "NewsRefreshInterval",
                "NewsPreBlockMinutes",
                "NewsPostBlockMinutes",
                "NewsBlockHighImpact",
                "NewsBlockMediumImpact",
                "NewsBlockLowImpact",
                "NewsSendAlerts",
                "NewsAlertInterval",
                "NewsAlertWavFileName",
                "NewsDefaultTextColor",
                "NewsWarningTextColor",
                "NewsBackgroundColor",
                "NewsHeaderColor",
                "NewsHighImpactColor",
                "NewsMediumImpactColor",
                "NewsLowImpactColor",
                "NewsDefaultFont",
                "NewsWarningFont",
                "NewsDebug"
            };

            foreach (string p in toRemove)
            {
                if (col[p] != null)
                    col.Remove (col[p]);
            }
        }

        private void ModifyIndicatorSettingsProperties (PropertyDescriptorCollection col)
        {
            if (ShowIndicatorSettings)
                return;

            // Hide every indicator-specific property when the master toggle is off.
            string[] toRemove = new[]
            {
                // KingOrderBlock
                "King_SwingPointNeighborhood",
                "King_ImbalanceQualifying",
                "King_OrderBlockFindingBosChochPeriod",
                "King_OrderBlockAge",
                "King_OrderBlocksSameDirectionOffset",
                "King_OrderBlocksDifferenceDirectionOffset",
                "King_SignalTradeQuantityPerOrderBlock",
                "King_SignalTradeSplitBars",
                // PANAKanal
                "Pana_Period",
                "Pana_Factor",
                "Pana_MiddlePeriod",
                "Pana_SignalBreakSplit",
                "Pana_SignalPullbackFindingPeriod",
                // ThunderZilla
                "Thunder_TrendMAType",
                "Thunder_TrendPeriod",
                "Thunder_TrendSmoothingEnabled",
                "Thunder_TrendSmoothingMethod",
                "Thunder_TrendSmoothingPeriod",
                "Thunder_StopOffsetMultiplierStop",
                "Thunder_SignalQuantityPerFlat",
                "Thunder_SignalQuantityPerTrend",
                // SumoPullback
                "Sumo_SlowMAType",
                "Sumo_SlowMAPeriod",
                "Sumo_SlowMASmoothingEnabled",
                "Sumo_SlowMASmoothingMethod",
                "Sumo_SlowMASmoothingPeriod",
                "Sumo_FastMA1Type",
                "Sumo_FastMA1Period",
                "Sumo_FastMA1SmoothingEnabled",
                "Sumo_FastMA1SmoothingMethod",
                "Sumo_FastMA1SmoothingPeriod",
                "Sumo_FastMA2Type",
                "Sumo_FastMA2Period",
                "Sumo_FastMA2SmoothingEnabled",
                "Sumo_FastMA2SmoothingMethod",
                "Sumo_FastMA2SmoothingPeriod",
                "Sumo_FastMA3Type",
                "Sumo_FastMA3Period",
                "Sumo_FastMA3SmoothingEnabled",
                "Sumo_FastMA3SmoothingMethod",
                "Sumo_FastMA3SmoothingPeriod",
                "Sumo_SignalSplitFirst",
                "Sumo_SignalSplitSecond",
                // SuperJumpBoost
                "SJB_SensitiveModeEnabled",
                "SJB_OffsetLevel1",
                "SJB_OffsetLevel2",
                "SJB_OffsetLevel3",
                "SJB_OffsetLevel4",
                "SJB_OffsetBase",
                "SJB_ReferencePricePeriod",
                "SJB_LineLevelsOffset",
                "SJB_ExtremeNeighborhood",
                "SJB_SignalCloseThreshold",
                "SJB_SignalQuantityPerZone",
                "SJB_SignalSplit",
                // Visuals
                "PanaZilliaBrush",
                "KingZillaBrush",
                "KingPanaBrush",
                "SumoBrush",
                "SjbBrush",
                "ArrowOffset",
            };

            foreach (string p in toRemove)
            {
                if (col[p] != null)
                    col.Remove (col[p]);
            }
        }

        public AttributeCollection GetAttributes () => TypeDescriptor.GetAttributes (GetType ());
        public string GetClassName () => TypeDescriptor.GetClassName (GetType ());
        public string GetComponentName () => TypeDescriptor.GetComponentName (GetType ());
        public TypeConverter GetConverter () => TypeDescriptor.GetConverter (GetType ());
        public EventDescriptor GetDefaultEvent () => TypeDescriptor.GetDefaultEvent (GetType ());
        public PropertyDescriptor GetDefaultProperty () => TypeDescriptor.GetDefaultProperty (GetType ());
        public object GetEditor (Type editorBaseType) => TypeDescriptor.GetEditor (GetType (), editorBaseType);
        public EventDescriptorCollection GetEvents (Attribute[] attributes) => TypeDescriptor.GetEvents (GetType (), attributes);
        public EventDescriptorCollection GetEvents () => TypeDescriptor.GetEvents (GetType ());

        public PropertyDescriptorCollection GetProperties (Attribute[] attributes)
        {
            PropertyDescriptorCollection orig = TypeDescriptor.GetProperties(GetType(), attributes);
            PropertyDescriptor[] arr = new PropertyDescriptor[orig.Count];
            orig.CopyTo (arr, 0);
            PropertyDescriptorCollection col = new PropertyDescriptorCollection(arr);
            ModifyPNLProperties (col);
            ModifySessionProperties (col);
            ModifyEmaFilterProperties (col);
            ModifyNewsFilterProperties (col);
            ModifyIndicatorSettingsProperties (col);
            return col;
        }

        public PropertyDescriptorCollection GetProperties () => GetProperties (new Attribute[0]);
        public object GetPropertyOwner (PropertyDescriptor pd) => this;
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [ReadOnly (true)]
        [Display (Name = "Strategy Name", GroupName = "Strategy Information", Order = 0)]
        public string StrategyName
        {
            get; set;
        }

        [ReadOnly (true)]
        [Display (Name = "Strategy Version", GroupName = "Strategy Information", Order = 1)]
        public string StrategyVersion => _strategyVersion;

        [NinjaScriptProperty]
        [ReadOnly (true)]
        [Display (Name = "Author", GroupName = "Strategy Information", Order = 2)]
        public string Author
        {
            get; set;
        }

        [NinjaScriptProperty]
        [ReadOnly (true)]
        [Display (Name = "Strategy Credits", GroupName = "Strategy Information", Order = 3)]
        public string StrategyCredits => Credits;

        [TypeConverter (typeof (FriendlyAtmConverter))]
        [PropertyEditor ("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        [Display (Name = "Atm Strategy", Order = 0, GroupName = "ATM Parameters", Description = "Select an existing NT8 ATM template.")]
        public string AtmStrategy
        {
            get; set;
        }

        // ==================== Display ====================
        [NinjaScriptProperty]
        [Display (Name = "Show Dashboard", Order = 0, GroupName = "Display",
            Description = "Show the SharpDX info panel (PnL/session/status). Turn off for stability mode — disables all dashboard rendering and dispatcher invalidate work. Position can be set via Dashboard Position; setting Position = Hidden has the same effect.")]
        public bool ShowDashboard
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Dashboard Position", Order = 1, GroupName = "Display",
            Description = "Where to anchor the SharpDX info panel on the chart. Hidden = same as Show Dashboard = false.")]
        public HudCorner DashboardPosition
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Dashboard Size", Order = 2, GroupName = "Display",
            Description = "Tiny / Small / Normal / Large / Huge. Drives both font size AND box width — the box tightens to fit the actual text at each preset, no empty real-estate. Changes apply on the next frame (free to tweak at runtime).")]
        public GodZillaHudSize DashboardSize
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show Control Panel", Order = 10, GroupName = "Display",
            Description = "Show the WPF arm/auto-arm/close-all button panel. Disable if you only need automated entries and don't want manual override controls.")]
        public bool ShowControlPanel
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Control Panel Position", Order = 11, GroupName = "Display",
            Description = "Where to anchor the button panel on the chart. Position changes apply on next strategy enable (the WPF panel is constructed once at strategy start). Hidden = same as Show Control Panel = false.")]
        public HudCorner ControlPanelPosition
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Enable Signal Tracking", Order = 0, GroupName = "Signals")]
        [RefreshProperties (RefreshProperties.All)]
        public bool EnableSignalTracking
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use PanaZillia Signals", Order = 1, GroupName = "Signals")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UsePanaZilliaSignals
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use KingZilla Signals", Order = 2, GroupName = "Signals")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseKingZillaSignals
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use KingPana Signals", Order = 3, GroupName = "Signals")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseKingPanaSignals
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use SumoPullback Signals", Order = 4, GroupName = "Signals")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseSumoPullbackSignals
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use SuperJumpBoost Signals", Order = 5, GroupName = "Signals")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseSuperJumpBoostSignals
        {
            get; set;
        }

        // ==================== Group Triggers ====================
        [NinjaScriptProperty]
        [Display (Name = "Enable Group-of-2 Trigger", Order = 0, GroupName = "Group Triggers",
            Description = "Enter only when at least 2 selected indicators (above) align on direction within the rolling window. Loosest consensus — fires more often than G3+. If multiple group toggles are on, the largest satisfied group wins.")]
        public bool EnableGroup2Trigger { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Enable Group-of-3 Trigger", Order = 1, GroupName = "Group Triggers",
            Description = "Enter only when at least 3 selected indicators (above) align on direction within the rolling window.")]
        public bool EnableGroup3Trigger { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Enable Group-of-4 Trigger", Order = 2, GroupName = "Group Triggers",
            Description = "Enter only when at least 4 selected indicators align on direction within the rolling window.")]
        public bool EnableGroup4Trigger { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Enable Group-of-5 Trigger", Order = 3, GroupName = "Group Triggers",
            Description = "Enter only when all 5 selected indicators align on direction within the rolling window. Requires all five Use*Signals to be on.")]
        public bool EnableGroup5Trigger { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Rolling Window (Bars)", Order = 10, GroupName = "Group Triggers",
            Description = "Lookback bars for collecting each selected indicator's most-recent direction when evaluating group consensus.")]
        public int RollingWindowBars { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Use EMA Filter", Order = 6, GroupName = "Signals", Description = "When enabled, longs require short EMA above long EMA and shorts require short EMA below long EMA.")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseEmaFilter
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Short EMA Period", Order = 7, GroupName = "Signals")]
        public int EmaShortPeriod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Long EMA Period", Order = 8, GroupName = "Signals")]
        public int EmaLongPeriod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use Unrealized PNL", Order = 0, GroupName = "Risk Management", Description = "If true, checks limits tick-by-tick including ATM open profit.")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseUnrealizedPnl
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use Daily Profit Target", Order = 1, GroupName = "Risk Management")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseDailyProfitTarget
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, double.MaxValue)]
        [Display (Name = "Daily Profit Target ($)", Order = 2, GroupName = "Risk Management")]
        public double DailyProfitTarget
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use Daily Loss Limit", Order = 3, GroupName = "Risk Management")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseDailyLossLimit
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, double.MaxValue)]
        [Display (Name = "Daily Loss Limit ($)", Order = 4, GroupName = "Risk Management", Description = "Positive Number (e.g. 500 for -$500 limit)")]
        public double DailyLossLimit
        {
            get; set;
        }

        [Display (Name = "Enable TF 1", Order = 1, GroupName = "Session Parameters")]
        [RefreshProperties (RefreshProperties.All)]
        public bool EnableTF1
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "Start Time 1", Order = 2, GroupName = "Session Parameters")]
        public DateTime StartTime1
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "End Time 1", Order = 3, GroupName = "Session Parameters")]
        public DateTime EndTime1
        {
            get; set;
        }

        [Display (Name = "Flatten at End TF 1", Order = 4, GroupName = "Session Parameters")]
        public bool FlattenTF1
        {
            get; set;
        }

        [Display (Name = "Enable TF 2", Order = 5, GroupName = "Session Parameters")]
        [RefreshProperties (RefreshProperties.All)]
        public bool EnableTF2
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "Start Time 2", Order = 6, GroupName = "Session Parameters")]
        public DateTime StartTime2
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "End Time 2", Order = 7, GroupName = "Session Parameters")]
        public DateTime EndTime2
        {
            get; set;
        }

        [Display (Name = "Flatten at End TF 2", Order = 8, GroupName = "Session Parameters")]
        public bool FlattenTF2
        {
            get; set;
        }

        [Display (Name = "Enable TF 3", Order = 9, GroupName = "Session Parameters")]
        [RefreshProperties (RefreshProperties.All)]
        public bool EnableTF3
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "Start Time 3", Order = 10, GroupName = "Session Parameters")]
        public DateTime StartTime3
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "End Time 3", Order = 11, GroupName = "Session Parameters")]
        public DateTime EndTime3
        {
            get; set;
        }

        [Display (Name = "Flatten at End TF 3", Order = 12, GroupName = "Session Parameters")]
        public bool FlattenTF3
        {
            get; set;
        }

        [Display (Name = "Enable Skip Window", Order = 13, GroupName = "Session Parameters")]
        [RefreshProperties (RefreshProperties.All)]
        public bool EnableSkipTimeWindow
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "Skip Start Time", Order = 14, GroupName = "Session Parameters")]
        public DateTime SkipStartTime
        {
            get; set;
        }

        [NinjaScriptProperty]
        [PropertyEditor ("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display (Name = "Skip End Time", Order = 15, GroupName = "Session Parameters")]
        public DateTime SkipEndTime
        {
            get; set;
        }

        // ==================== News Filter ====================
        [NinjaScriptProperty]
        [Display (Name = "Enable News Filter", Order = 0, GroupName = "News Filter", Description = "Enable NewsSignals live news filter. Disabled automatically during Strategy Analyzer/backtest and Playback/Market Replay.")]
        [RefreshProperties (RefreshProperties.All)]
        public bool EnableNewsFilter
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Flatten/Cancel At News Warning", Order = 1, GroupName = "News Filter", Description = "If enabled, closes tracked ATM position and cancels working orders when the pre-news warning window starts.")]
        public bool NewsFlattenAtWarningTime
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show News Display", Order = 2, GroupName = "News Filter", Description = "Show or hide the NewsSignals chart display.")]
        public bool NewsShowDisplay
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Display Location", Order = 3, GroupName = "News Filter")]
        public NewsPrintLocation NewsDisplayLocation
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 5000)]
        [Display (Name = "Display X Offset Pixels", Order = 4, GroupName = "News Filter")]
        public int NewsDisplayXOffsetPixels
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 5000)]
        [Display (Name = "Display Y Offset Pixels", Order = 5, GroupName = "News Filter")]
        public int NewsDisplayYOffsetPixels
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use 24-Hour Time", Order = 6, GroupName = "News Filter")]
        public bool NewsUse24HourTime
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show Background", Order = 7, GroupName = "News Filter")]
        public bool NewsShowBackground
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show Time BackBrush", Order = 8, GroupName = "News Filter")]
        public bool NewsShowTimeBackBrush
        {
            get; set;
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Time BackBrush", Order = 9, GroupName = "News Filter")]
        public Brush NewsTimeBackBrush
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsTimeBackBrushSerialize
        {
            get
            {
                return Serialize.BrushToString (NewsTimeBackBrush);
            }
            set
            {
                NewsTimeBackBrush = Serialize.StringToBrush (value);
            }
        }

        [NinjaScriptProperty]
        [Display (Name = "US Events Only", Order = 10, GroupName = "News Filter")]
        public bool NewsUSOnlyEvents
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Today's News Only", Order = 11, GroupName = "News Filter")]
        public bool NewsTodaysNewsOnly
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show Low Priority", Order = 12, GroupName = "News Filter")]
        public bool NewsShowLowPriority
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Max News Items", Order = 13, GroupName = "News Filter")]
        public int NewsMaxNewsItems
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Refresh Interval Minutes", Order = 14, GroupName = "News Filter")]
        public int NewsRefreshInterval
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 240)]
        [Display (Name = "Pre-News Block Minutes", Order = 15, GroupName = "News Filter")]
        public int NewsPreBlockMinutes
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 240)]
        [Display (Name = "Post-News Block Minutes", Order = 16, GroupName = "News Filter")]
        public int NewsPostBlockMinutes
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Block High Impact", Order = 17, GroupName = "News Filter")]
        public bool NewsBlockHighImpact
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Block Medium Impact", Order = 18, GroupName = "News Filter")]
        public bool NewsBlockMediumImpact
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Block Low Impact", Order = 19, GroupName = "News Filter")]
        public bool NewsBlockLowImpact
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Send Alerts", Order = 20, GroupName = "News Filter")]
        public bool NewsSendAlerts
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 240)]
        [Display (Name = "Alert Interval Minutes", Order = 21, GroupName = "News Filter")]
        public int NewsAlertInterval
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Alert WAV File", Order = 22, GroupName = "News Filter")]
        public string NewsAlertWavFileName
        {
            get; set;
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Default Text Color", Order = 23, GroupName = "News Filter")]
        public Brush NewsDefaultTextColor
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsDefaultTextColorSerialize
        {
            get
            {
                return Serialize.BrushToString (NewsDefaultTextColor);
            }
            set
            {
                NewsDefaultTextColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Warning Text Color", Order = 24, GroupName = "News Filter")]
        public Brush NewsWarningTextColor
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsWarningTextColorSerialize
        {
            get
            {
                return Serialize.BrushToString (NewsWarningTextColor);
            }
            set
            {
                NewsWarningTextColor = Serialize.StringToBrush (value);
            }
        }

        private static readonly Brush _newsBackgroundColorDefault = MakeFrozenBrush (170, 0, 0, 0);
        private static Brush MakeFrozenBrush (byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush (Color.FromArgb (a, r, g, b));
            br.Freeze ();
            return br;
        }
        private Brush _newsBackgroundColor;

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Background Color", Order = 25, GroupName = "News Filter")]
        public Brush NewsBackgroundColor
        {
            get
            {
                return _newsBackgroundColor ?? _newsBackgroundColorDefault;
            }
            set
            {
                _newsBackgroundColor = value;
            }
        }

        [Browsable (false)]
        public string NewsBackgroundColorSerialize
        {
            get
            {
                return Serialize.BrushToString (_newsBackgroundColor ?? _newsBackgroundColorDefault);
            }
            set
            {
                _newsBackgroundColor = Serialize.StringToBrush (value) ?? _newsBackgroundColorDefault;
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Header Text Color", Order = 26, GroupName = "News Filter")]
        public Brush NewsHeaderColor
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsHeaderColorSerialize
        {
            get
            {
                return Serialize.BrushToString (NewsHeaderColor);
            }
            set
            {
                NewsHeaderColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "High Impact Text Color", Order = 27, GroupName = "News Filter")]
        public Brush NewsHighImpactColor
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsHighImpactColorSerialize
        {
            get
            {
                return Serialize.BrushToString (NewsHighImpactColor);
            }
            set
            {
                NewsHighImpactColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Medium Impact Text Color", Order = 28, GroupName = "News Filter")]
        public Brush NewsMediumImpactColor
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsMediumImpactColorSerialize
        {
            get
            {
                return Serialize.BrushToString (NewsMediumImpactColor);
            }
            set
            {
                NewsMediumImpactColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Low Impact Text Color", Order = 29, GroupName = "News Filter")]
        public Brush NewsLowImpactColor
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsLowImpactColorSerialize
        {
            get
            {
                return Serialize.BrushToString (NewsLowImpactColor);
            }
            set
            {
                NewsLowImpactColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Default Font", Order = 30, GroupName = "News Filter")]
        public SimpleFont NewsDefaultFont
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsDefaultFontSerialize
        {
            get
            {
                return NewsDefaultFont.FamilySerialize;
            }
            set
            {
                NewsDefaultFont = new SimpleFont (value, NewsDefaultFontSizeSerialize);
            }
        }

        [Browsable (false)]
        public double NewsDefaultFontSizeSerialize
        {
            get
            {
                return NewsDefaultFont.Size;
            }
            set
            {
                NewsDefaultFont.Size = value;
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Warning Font", Order = 31, GroupName = "News Filter")]
        public SimpleFont NewsWarningFont
        {
            get; set;
        }

        [Browsable (false)]
        public string NewsWarningFontSerialize
        {
            get
            {
                return NewsWarningFont.FamilySerialize;
            }
            set
            {
                NewsWarningFont = new SimpleFont (value, NewsWarningFontSizeSerialize);
            }
        }

        [Browsable (false)]
        public double NewsWarningFontSizeSerialize
        {
            get
            {
                return NewsWarningFont.Size;
            }
            set
            {
                NewsWarningFont.Size = value;
            }
        }

        [NinjaScriptProperty]
        [Display (Name = "Debug", Order = 32, GroupName = "News Filter")]
        public bool NewsDebug
        {
            get; set;
        }

        // ==================== Indicator Settings master toggle ====================
        [NinjaScriptProperty]
        [Display (Name = "Show Indicator Settings", Order = 0, GroupName = "Indicator Settings", Description = "When checked, exposes all gbKingPanaZilla indicator parameters below.")]
        [RefreshProperties (RefreshProperties.All)]
        public bool ShowIndicatorSettings
        {
            get; set;
        }

        // ==================== KingOrderBlock ====================
        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Swing Point: Neighborhood", Order = 0, GroupName = "Indicator: KingOrderBlock")]
        public int King_SwingPointNeighborhood
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Imbalance: Qualifying (Bars)", Order = 10, GroupName = "Indicator: KingOrderBlock")]
        public int King_ImbalanceQualifying
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Order Block: Finding BOS/CHoCH Period", Order = 20, GroupName = "Indicator: KingOrderBlock")]
        public int King_OrderBlockFindingBosChochPeriod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, int.MaxValue)]
        [Display (Name = "Order Block: Age (Bars)", Order = 30, GroupName = "Indicator: KingOrderBlock")]
        public int King_OrderBlockAge
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, int.MaxValue)]
        [Display (Name = "Order Blocks: Same Direction Offset (Ticks)", Order = 40, GroupName = "Indicator: KingOrderBlock")]
        public int King_OrderBlocksSameDirectionOffset
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, int.MaxValue)]
        [Display (Name = "Order Blocks: Diff Direction Offset (Ticks)", Order = 50, GroupName = "Indicator: KingOrderBlock")]
        public int King_OrderBlocksDifferenceDirectionOffset
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Trade: Quantity Per OB", Order = 60, GroupName = "Indicator: KingOrderBlock")]
        public int King_SignalTradeQuantityPerOrderBlock
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Trade: Split (Bars)", Order = 70, GroupName = "Indicator: KingOrderBlock")]
        public int King_SignalTradeSplitBars
        {
            get; set;
        }

        // ==================== PANAKanal ====================
        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Period", Order = 0, GroupName = "Indicator: PANAKanal")]
        public int Pana_Period
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0.01, double.MaxValue)]
        [Display (Name = "Factor", Order = 10, GroupName = "Indicator: PANAKanal")]
        public double Pana_Factor
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Middle Period", Order = 20, GroupName = "Indicator: PANAKanal")]
        public int Pana_MiddlePeriod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Break Split (Bars)", Order = 30, GroupName = "Indicator: PANAKanal")]
        public int Pana_SignalBreakSplit
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Pullback Finding Period", Order = 40, GroupName = "Indicator: PANAKanal")]
        public int Pana_SignalPullbackFindingPeriod
        {
            get; set;
        }

        // ==================== ThunderZilla ====================
        [NinjaScriptProperty]
        [Display (Name = "Trend: MA Type", Order = 0, GroupName = "Indicator: ThunderZilla")]
        public gbThunderZillaMAType Thunder_TrendMAType
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Trend: Period", Order = 10, GroupName = "Indicator: ThunderZilla")]
        public int Thunder_TrendPeriod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Trend: Smoothing Enabled", Order = 20, GroupName = "Indicator: ThunderZilla")]
        public bool Thunder_TrendSmoothingEnabled
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Trend: Smoothing Method", Order = 30, GroupName = "Indicator: ThunderZilla")]
        public gbThunderZillaMAType Thunder_TrendSmoothingMethod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Trend: Smoothing Period", Order = 40, GroupName = "Indicator: ThunderZilla")]
        public int Thunder_TrendSmoothingPeriod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0.0, double.MaxValue)]
        [Display (Name = "Stop: Offset Multiplier (Ticks)", Order = 50, GroupName = "Indicator: ThunderZilla")]
        public double Thunder_StopOffsetMultiplierStop
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal: Quantity Per Flat", Order = 60, GroupName = "Indicator: ThunderZilla")]
        public int Thunder_SignalQuantityPerFlat
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal: Quantity Per Trend", Order = 70, GroupName = "Indicator: ThunderZilla")]
        public int Thunder_SignalQuantityPerTrend
        {
            get; set;
        }

        // ==================== SumoPullback ====================
        [NinjaScriptProperty]
        [Display (Name = "Slow MA: Type", Order = 0, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_SlowMAType { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Slow MA: Period", Order = 1, GroupName = "Indicator: SumoPullback")]
        public int Sumo_SlowMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Slow MA: Smoothing Enabled", Order = 2, GroupName = "Indicator: SumoPullback")]
        public bool Sumo_SlowMASmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Slow MA: Smoothing Method", Order = 3, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_SlowMASmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Slow MA: Smoothing Period", Order = 4, GroupName = "Indicator: SumoPullback")]
        public int Sumo_SlowMASmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #1: Type", Order = 10, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_FastMA1Type { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Fast MA #1: Period", Order = 11, GroupName = "Indicator: SumoPullback")]
        public int Sumo_FastMA1Period { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #1: Smoothing Enabled", Order = 12, GroupName = "Indicator: SumoPullback")]
        public bool Sumo_FastMA1SmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #1: Smoothing Method", Order = 13, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_FastMA1SmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Fast MA #1: Smoothing Period", Order = 14, GroupName = "Indicator: SumoPullback")]
        public int Sumo_FastMA1SmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #2: Type", Order = 20, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_FastMA2Type { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Fast MA #2: Period", Order = 21, GroupName = "Indicator: SumoPullback")]
        public int Sumo_FastMA2Period { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #2: Smoothing Enabled", Order = 22, GroupName = "Indicator: SumoPullback")]
        public bool Sumo_FastMA2SmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #2: Smoothing Method", Order = 23, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_FastMA2SmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Fast MA #2: Smoothing Period", Order = 24, GroupName = "Indicator: SumoPullback")]
        public int Sumo_FastMA2SmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #3: Type", Order = 30, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_FastMA3Type { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Fast MA #3: Period", Order = 31, GroupName = "Indicator: SumoPullback")]
        public int Sumo_FastMA3Period { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #3: Smoothing Enabled", Order = 32, GroupName = "Indicator: SumoPullback")]
        public bool Sumo_FastMA3SmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display (Name = "Fast MA #3: Smoothing Method", Order = 33, GroupName = "Indicator: SumoPullback")]
        public gbSumoPullbackMAType Sumo_FastMA3SmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Fast MA #3: Smoothing Period", Order = 34, GroupName = "Indicator: SumoPullback")]
        public int Sumo_FastMA3SmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Split: First", Order = 40, GroupName = "Indicator: SumoPullback")]
        public int Sumo_SignalSplitFirst { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Split: Second", Order = 41, GroupName = "Indicator: SumoPullback")]
        public int Sumo_SignalSplitSecond { get; set; }

        // ==================== SuperJumpBoost ====================
        [NinjaScriptProperty]
        [Display (Name = "Sensitive Mode Enabled", Order = 0, GroupName = "Indicator: SuperJumpBoost")]
        public bool SJB_SensitiveModeEnabled { get; set; }

        [NinjaScriptProperty]
        [Range (0.01, double.MaxValue)]
        [Display (Name = "Offset Level 1", Order = 10, GroupName = "Indicator: SuperJumpBoost")]
        public double SJB_OffsetLevel1 { get; set; }

        [NinjaScriptProperty]
        [Range (0.01, double.MaxValue)]
        [Display (Name = "Offset Level 2", Order = 11, GroupName = "Indicator: SuperJumpBoost")]
        public double SJB_OffsetLevel2 { get; set; }

        [NinjaScriptProperty]
        [Range (0.01, double.MaxValue)]
        [Display (Name = "Offset Level 3", Order = 12, GroupName = "Indicator: SuperJumpBoost")]
        public double SJB_OffsetLevel3 { get; set; }

        [NinjaScriptProperty]
        [Range (0.01, double.MaxValue)]
        [Display (Name = "Offset Level 4", Order = 13, GroupName = "Indicator: SuperJumpBoost")]
        public double SJB_OffsetLevel4 { get; set; }

        [NinjaScriptProperty]
        [Range (0.01, double.MaxValue)]
        [Display (Name = "Offset Base", Order = 14, GroupName = "Indicator: SuperJumpBoost")]
        public double SJB_OffsetBase { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Reference Price Period", Order = 20, GroupName = "Indicator: SuperJumpBoost")]
        public int SJB_ReferencePricePeriod { get; set; }

        [NinjaScriptProperty]
        [Range (0, int.MaxValue)]
        [Display (Name = "Line Levels Offset", Order = 30, GroupName = "Indicator: SuperJumpBoost")]
        public int SJB_LineLevelsOffset { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Extreme Neighborhood", Order = 40, GroupName = "Indicator: SuperJumpBoost")]
        public int SJB_ExtremeNeighborhood { get; set; }

        [NinjaScriptProperty]
        [Range (1, 100)]
        [Display (Name = "Signal Close Threshold (%)", Order = 50, GroupName = "Indicator: SuperJumpBoost")]
        public int SJB_SignalCloseThreshold { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Quantity Per Zone", Order = 60, GroupName = "Indicator: SuperJumpBoost")]
        public int SJB_SignalQuantityPerZone { get; set; }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Signal Split (Bars)", Order = 70, GroupName = "Indicator: SuperJumpBoost")]
        public int SJB_SignalSplit { get; set; }

        // ==================== Indicator Visuals ====================
        [XmlIgnore]
        [Display (Name = "PanaZillia Color", Order = 0, GroupName = "Indicator: Visuals")]
        public Brush PanaZilliaBrush
        {
            get; set;
        }
        [Browsable (false)]
        public string PanaZilliaBrushSerialize
        {
            get
            {
                return Serialize.BrushToString (PanaZilliaBrush);
            }
            set
            {
                PanaZilliaBrush = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore]
        [Display (Name = "KingZilla Color", Order = 1, GroupName = "Indicator: Visuals")]
        public Brush KingZillaBrush
        {
            get; set;
        }
        [Browsable (false)]
        public string KingZillaBrushSerialize
        {
            get
            {
                return Serialize.BrushToString (KingZillaBrush);
            }
            set
            {
                KingZillaBrush = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore]
        [Display (Name = "KingPana Color", Order = 2, GroupName = "Indicator: Visuals")]
        public Brush KingPanaBrush
        {
            get; set;
        }
        [Browsable (false)]
        public string KingPanaBrushSerialize
        {
            get
            {
                return Serialize.BrushToString (KingPanaBrush);
            }
            set
            {
                KingPanaBrush = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore]
        [Display (Name = "SumoPullback Color", Order = 3, GroupName = "Indicator: Visuals")]
        public Brush SumoBrush
        {
            get; set;
        }
        [Browsable (false)]
        public string SumoBrushSerialize
        {
            get { return Serialize.BrushToString (SumoBrush); }
            set { SumoBrush = Serialize.StringToBrush (value); }
        }

        [XmlIgnore]
        [Display (Name = "SuperJumpBoost Color", Order = 4, GroupName = "Indicator: Visuals")]
        public Brush SjbBrush
        {
            get; set;
        }
        [Browsable (false)]
        public string SjbBrushSerialize
        {
            get { return Serialize.BrushToString (SjbBrush); }
            set { SjbBrush = Serialize.StringToBrush (value); }
        }

        [NinjaScriptProperty]
        [Range (0, int.MaxValue)]
        [Display (Name = "Arrow Offset (Ticks)", Order = 5, GroupName = "Indicator: Visuals")]
        public int ArrowOffset
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Log Trades", Order = 0, GroupName = "Logging", Description = "Write a CSV trade log to the NinjaTrader user data folder.")]
        public bool LogEnabled
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Enable Debug", Order = 1, GroupName = "Logging", Description = "Print diagnostic messages to the NinjaTrader output window.")]
        public bool EnableDebug
        {
            get; set;
        }
        #endregion

        #region AtmStrategySelector converter
        public class FriendlyAtmConverter : TypeConverter
        {
            public override StandardValuesCollection GetStandardValues (ITypeDescriptorContext context)
            {
                List<string> values = new List<string>();
                string atmDir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy");

                if (System.IO.Directory.Exists (atmDir))
                {
                    string[] files = System.IO.Directory.GetFiles(atmDir, "*.xml");
                    foreach (string atm in files)
                    {
                        string atmName = System.IO.Path.GetFileNameWithoutExtension(atm);
                        values.Add (atmName);
                        NinjaTrader.Code.Output.Process (atmName, PrintTo.OutputTab1);
                    }
                }

                return new StandardValuesCollection (values);
            }

            public override object ConvertFrom (ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                return value?.ToString () ?? string.Empty;
            }

            public override object ConvertTo (ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
            {
                return value;
            }

            public override bool CanConvertFrom (ITypeDescriptorContext context, Type sourceType)
            {
                return true;
            }
            public override bool CanConvertTo (ITypeDescriptorContext context, Type destinationType)
            {
                return true;
            }
            public override bool GetStandardValuesExclusive (ITypeDescriptorContext context)
            {
                return true;
            }
            public override bool GetStandardValuesSupported (ITypeDescriptorContext context)
            {
                return true;
            }
        }
        #endregion
    }
}