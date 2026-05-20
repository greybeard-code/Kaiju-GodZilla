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
using NewsPrintLocation = NinjaTrader.NinjaScript.Indicators.Playr101.NewsSignals.NewsPrintLocation;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies.Playr101
{
    #region GUI Categories
    [CategoryOrder ("Strategy Information", 0)]
    [CategoryOrder ("ATM Parameters", 1)]
    [CategoryOrder ("Button Panel", 2)]
    [CategoryOrder ("Signals", 3)]
    [CategoryOrder ("Risk Management", 4)]
    [CategoryOrder ("News Filter", 5)]
    [CategoryOrder ("Session Parameters", 6)]
    [CategoryOrder ("Indicator Settings", 7)]
    [CategoryOrder ("Indicator: KingOrderBlock", 8)]
    [CategoryOrder ("Indicator: PANAKanal", 9)]
    [CategoryOrder ("Indicator: ThunderZilla", 10)]
    [CategoryOrder ("Indicator: Visuals", 11)]
    [CategoryOrder ("Logging", 12)]
    #endregion

    public class gbKingPanaZillaKillah : Strategy, ICustomTypeDescriptor
    {
        public override string DisplayName => Name;

        #region Variables
        // Drawing
        SimpleFont title = new SimpleFont("Agency Fb", 16) { Size = 20, Bold = true };

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

        // Naked-position watchdog
        private DateTime lastNakedCheck = Core.Globals.MinDate;
        private const int NakedCheckIntervalSeconds = 3;

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

        private bool activeTradeUsesPanaZillia = false;
        private bool activeTradeUsesKingZilla = false;
        private bool activeTradeUsesKingPana = false;
        private MarketPosition activeTradeDirection = MarketPosition.Flat;
        private string lastTradeClosedSummary = string.Empty;

        private string _version = "1.5.3";

        // Indicator
        private gbKingPanaZilla _gbIndicator;

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
                Description = "Strategy utilizing gbKingPanaZilla signals.";
                Name = "gbKingPanaZillaKillah";
                StrategyName = Name;
                _version = "1.5.4";

                Author = "Playr101";
                Credits = "GreyBeard";

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

                //--------- User Configurable Parameters ---------//
                // ATM Strategy
                AtmStrategy = string.Empty;

                // Signal Usage
                EnableSignalTracking = false;
                UsePanaZilliaSignals = true;
                UseKingZillaSignals = true;
                UseKingPanaSignals = true;

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
                StartTime1 = DateTime.Parse ("09:30", System.Globalization.CultureInfo.InvariantCulture);
                EndTime1 = DateTime.Parse ("12:00", System.Globalization.CultureInfo.InvariantCulture);
                FlattenTF1 = true;
                EnableTF2 = true;
                StartTime2 = DateTime.Parse ("13:00", System.Globalization.CultureInfo.InvariantCulture);
                EndTime2 = DateTime.Parse ("15:30", System.Globalization.CultureInfo.InvariantCulture);
                FlattenTF2 = true;

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

                // Indicator visual defaults
                PanaZilliaBrush = Brushes.Cyan;
                KingZillaBrush = Brushes.DodgerBlue;
                KingPanaBrush = Brushes.LimeGreen;
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
                        "gbKPZKillah_" + safeAccount + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                    _logWriter = new StreamWriter (logPath, append: false, encoding: Encoding.UTF8);
                    _logWriter.WriteLine ("OpenTime,Account,Instrument,OpenPrice,Qty,CloseTime,Trigger,Direction,AtmStrategy,RealizedPnL");
                    _logWriter.Flush ();
                }

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
                if (_logWriter != null)
                {
                    _logWriter.Flush ();
                    _logWriter.Dispose ();
                    _logWriter = null;
                }

                RemoveRBroControlPanel ();
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
            double pz = _gbIndicator.PanaZillia_Trade[0];
            double kz = _gbIndicator.KingZilla_Trade[0];
            double kp = _gbIndicator.KingPana_Trade[0];

            bool panaLong = pz == 1 && UsePanaZilliaSignals;
            bool kingZillaLong = kz == 1 && UseKingZillaSignals;
            bool kingPanaLong = kp == 1 && UseKingPanaSignals;
            bool panaShort = pz == -1 && UsePanaZilliaSignals;
            bool kingZillaShort = kz == -1 && UseKingZillaSignals;
            bool kingPanaShort = kp == -1 && UseKingPanaSignals;

            bool goLong = panaLong || kingZillaLong || kingPanaLong;
            bool goShort = panaShort || kingZillaShort || kingPanaShort;

            // Optional EMA trade filter: longs require short EMA > long EMA, shorts require short EMA < long EMA.
            if (UseEmaFilter && _emaShortFilter != null && _emaLongFilter != null && CurrentBar >= Math.Max (EmaShortPeriod, EmaLongPeriod))
            {
                bool bullishEmaAlignment = _emaShortFilter[0] > _emaLongFilter[0];
                bool bearishEmaAlignment = _emaShortFilter[0] < _emaLongFilter[0];

                if (goLong && !bullishEmaAlignment)
                {
                    goLong = false;
                    panaLong = kingZillaLong = kingPanaLong = false;
                }
                if (goShort && !bearishEmaAlignment)
                {
                    goShort = false;
                    panaShort = kingZillaShort = kingPanaShort = false;
                }
            }

            // Submit ATM entry only when both ids are reset
            if (orderId.Length == 0 && atmStrategyId.Length == 0 && goLong && _armLong)
            {
                string trigger = string.Join("+", new[] {
                    panaLong ? "PZ" : null,
                    kingZillaLong ? "KZ" : null,
                    kingPanaLong ? "KP" : null,
                }.Where(s => s != null));

                isAtmStrategyCreated = false;
                SetActiveTradeSignalSources (panaLong, kingZillaLong, kingPanaLong, MarketPosition.Long);
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
                }.Where(s => s != null));

                isAtmStrategyCreated = false;
                SetActiveTradeSignalSources (panaShort, kingZillaShort, kingPanaShort, MarketPosition.Short);
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

            CheckForNakedPositions (tickTime);
            DrawPnlDisplay ();
            UpdateRBroStatusUI ();
        }

        private void DrawPnlDisplay ()
        {
            int displayTime = 0;

            if (BarsArray.Length > 1 && CurrentBars.Length > 1 && CurrentBars[1] >= 0)
                displayTime = ToTime (Times[1][0]);
            else if (CurrentBar >= 0)
                displayTime = ToTime (Time[0]);

            bool timeFilterEnabled = EnableTF1 || EnableTF2;
            bool inSession = !timeFilterEnabled || CheckTradingTimeframes(displayTime);

            string sessionText = timeFilterEnabled
                ? (inSession ? "Trading: IN SESSION" : "Trading: OUT OF SESSION")
                : "Trading: IN SESSION (Time Filter Disabled)";

            string enabledText = _strategyEnabled
                ? "Strategy: ENABLED | LONGS:" + (_armLong ? "ON" : "OFF") + " SHORTS:" + (_armShort ? "ON" : "OFF")
                : "Strategy: DISABLED";

            string targetStr = UseDailyProfitTarget ? "$" + DailyProfitTarget.ToString("F0") : "~";
            string lossStr = UseDailyLossLimit ? "-$" + DailyLossLimit.ToString("F0") : "~";
            double currentDisplayUnrealized = UseUnrealizedPnl ? dailyUnrealizedPnL : 0.0;

            string pnlLine1 = dailyLimitHit && !string.IsNullOrEmpty(dailyPnlStatusMessage)
                ? dailyPnlStatusMessage
                : "Total: $" + totalRunningPnL.ToString("F2")
                + "  |  Closed: $" + dailyRealizedPnL.ToString("F2")
                + (UseUnrealizedPnl ? "  |  Open: $" + currentDisplayUnrealized.ToString("F2") : string.Empty);

            string pnlLine2 = "Target: " + targetStr + "  |  Max Loss: " + lossStr;

            List<string> lines = new List<string>
            {
                "--- " + Name + " v" + StrategyVersion + " ---",
                enabledText,
                sessionText
            };

            if (EnableNewsFilter)
                lines.Add (BuildNewsFilterDisplayLine ());

            lines.Add (pnlLine1);
            lines.Add (pnlLine2);

            if (EnableSignalTracking)
            {
                if (!string.IsNullOrEmpty (lastTradeClosedSummary))
                    lines.Add (lastTradeClosedSummary);

                lines.AddRange (BuildSignalTrackingDisplayLines ());
            }

            string labelText = string.Join("\n", lines);

            Brush textBrush = dailyLimitHit
                ? (dailyPnlStatusMessage.Contains("PROFIT") ? Brushes.Lime : Brushes.Red)
                : ((dailyRealizedPnL + currentDisplayUnrealized) >= 0 ? Brushes.Lime : Brushes.Red);

            Draw.TextFixed (this, "PnlDisplay", labelText,
                TextPosition.BottomRight, textBrush, new SimpleFont ("Arial", 11),
                Brushes.Transparent, Brushes.Black, 80);

            if (dailyLimitHit && !string.IsNullOrEmpty (dailyPnlStatusMessage))
            {
                Brush statusBrush = dailyPnlStatusMessage.Contains("PROFIT") ? Brushes.Lime : Brushes.Red;

                Draw.TextFixed (this, "PnlStatus", dailyPnlStatusMessage,
                    TextPosition.Center, statusBrush, title, Brushes.Black, Brushes.Black, 0);
            }
            else
            {
                RemoveDrawObject ("PnlStatus");
            }
        }

        private void SetActiveTradeSignalSources (bool usePanaZillia, bool useKingZilla, bool useKingPana, MarketPosition direction)
        {
            activeTradeUsesPanaZillia = usePanaZillia;
            activeTradeUsesKingZilla = useKingZilla;
            activeTradeUsesKingPana = useKingPana;
            activeTradeDirection = direction;
        }

        private void ClearActiveTradeSignalSources ()
        {
            activeTradeUsesPanaZillia = false;
            activeTradeUsesKingZilla = false;
            activeTradeUsesKingPana = false;
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

            lines.Insert (0, enabledSignals.Count > 0 ? "Enabled Signals: " + string.Join (", ", enabledSignals) : "Enabled Signals: None");
            return lines;
        }

        private bool CheckTradingTimeframes (int currentTime)
        {
            // If no TF filters are enabled, allow trading all day
            if (!EnableTF1 && !EnableTF2)
                return true;

            bool tf1 = EnableTF1 && IsTimeInWindow(currentTime, ToTime(StartTime1), ToTime(EndTime1));
            bool tf2 = EnableTF2 && IsTimeInWindow(currentTime, ToTime(StartTime2), ToTime(EndTime2));

            return tf1 || tf2;
        }

        private void CheckFlattenTimeframes (int currentTime)
        {
            // If no TF filters are enabled, do not do any TF-based flattening
            if (!EnableTF1 && !EnableTF2)
                return;

            if (CurrentBar < 1)
                return;

            int previousTime = ToTime(Time[1]);

            bool flatten1 = EnableTF1 && FlattenTF1
                && IsTimeInWindow(previousTime, ToTime(StartTime1), ToTime(EndTime1))
                && !IsTimeInWindow(currentTime, ToTime(StartTime1), ToTime(EndTime1));

            bool flatten2 = EnableTF2 && FlattenTF2
                && IsTimeInWindow(previousTime, ToTime(StartTime2), ToTime(EndTime2))
                && !IsTimeInWindow(currentTime, ToTime(StartTime2), ToTime(EndTime2));

            if (flatten1 || flatten2)
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

            if (EnableDebug)
                Print ($"[{Name}] FlattenEverything: {reason}");

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
            if (State != State.Realtime)
                return;

            if ((tickTime - lastNakedCheck).TotalSeconds < NakedCheckIntervalSeconds)
                return;

            lastNakedCheck = tickTime;

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
                        Margin = new Thickness (10, 10, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    };

                    StackPanel main = new StackPanel { Orientation = Orientation.Vertical };

                    main.Children.Add (new TextBlock
                    {
                        Text = "⚡ KPZ Killah ⚡",
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
                        Width = 80,
                        Height = 30,
                        Margin = new Thickness (2),
                        Background = Brushes.DarkGreen,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold
                    };
                    _armLongBtn.Click += ArmLongBtn_Click;
                    btnRow.Children.Add (_armLongBtn);

                    _armShortBtn = new Button
                    {
                        Content = "ARM SHORT",
                        Width = 80,
                        Height = 30,
                        Margin = new Thickness (2),
                        Background = Brushes.DarkRed,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold
                    };
                    _armShortBtn.Click += ArmShortBtn_Click;
                    btnRow.Children.Add (_armShortBtn);

                    main.Children.Add (btnRow);

                    _autoArmBtn = new Button
                    {
                        Content = "AUTO ARM: OFF",
                        Width = 166,
                        Height = 30,
                        Margin = new Thickness (2, 4, 2, 0),
                        Background = Brushes.DimGray,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold
                    };
                    _autoArmBtn.Click += AutoArmBtn_Click;
                    main.Children.Add (_autoArmBtn);

                    _closeBtn = new Button
                    {
                        Content = "CLOSE ALL",
                        Width = 166,
                        Height = 30,
                        Margin = new Thickness (2, 4, 2, 0),
                        Background = Brushes.Maroon,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold
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
            else
                _armLong = _armShort = false;

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
                    string selectedAtm = !string.IsNullOrWhiteSpace (AtmStrategy) ? AtmStrategy : "No ATM Selected";

                    string pending = orderId.Length > 0
                ? " [ORDER: " + selectedAtm + "]"
                : (!string.IsNullOrEmpty (atmStrategyId) ? " [ATM: " + selectedAtm + "]" : "");

                    _statusLabel.Content = pos + pending;

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
                // Visuals
                "PanaZilliaBrush",
                "KingZillaBrush",
                "KingPanaBrush",
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
        public string StrategyVersion => _version;

        [NinjaScriptProperty]
        [ReadOnly (true)]
        [Display (Name = "Author", GroupName = "Strategy Information", Order = 2)]
        public string Author
        {
            get; set;
        }

        [NinjaScriptProperty]
        [ReadOnly (true)]
        [Display (Name = "Credits", GroupName = "Strategy Information", Order = 3)]
        public string Credits
        {
            get; set;
        }

        [TypeConverter (typeof (FriendlyAtmConverter))]
        [PropertyEditor ("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
        [Display (Name = "Atm Strategy", Order = 0, GroupName = "ATM Parameters", Description = "Select an existing NT8 ATM template.")]
        public string AtmStrategy
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
        [Display (Name = "Use EMA Crossover Filter", Order = 4, GroupName = "Signals", Description = "When enabled, longs require short EMA above long EMA and shorts require short EMA below long EMA.")]
        [RefreshProperties (RefreshProperties.All)]
        public bool UseEmaFilter
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Short EMA Period", Order = 5, GroupName = "Signals")]
        public int EmaShortPeriod
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (1, int.MaxValue)]
        [Display (Name = "Long EMA Period", Order = 6, GroupName = "Signals")]
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

        [NinjaScriptProperty]
        [Range (0, int.MaxValue)]
        [Display (Name = "Arrow Offset (Ticks)", Order = 3, GroupName = "Indicator: Visuals")]
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