#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// ============================================================
//  gbKingPanaZilla  â€” GreyBeard composite signal indicator
//
//  Loads gbKingOrderBlock, gbPANAKanal, and gbThunderZilla and
//  emits three cross-system trade signal plots:
//
//  PanaZilla_Trade  â€” PanaKanal >= 2  AND Thunder >= 3  â†’  +1
//                    â€” PanaKanal <= -2 AND Thunder <= -3 â†’  -1
//  KingZilla_Trade   â€” Thunder  >= 3   AND KingOrder >= 1  â†’  +1
//                    â€” Thunder  <= -3  AND KingOrder <= -1 â†’  -1
//  KingPana_Trade    â€” PanaKanal >= 2  AND KingOrder >= 1  â†’  +1
//                    â€” PanaKanal <= -2 AND KingOrder <= -1 â†’  -1
//
//  Plot values: +1 = buy signal, -1 = sell signal, 0 = no signal.
//
//  gbPANAKanal Signal_Trade:
//    1 = Trend Start Up,   -1 = Trend Start Down
//    2 = Break Up,         -2 = Break Down
//    3 = Pullback Bullish, -3 = Pullback Bearish
//
//  gbThunderZilla Signal_Trade:
//    1 = Uptrend Start,    -1 = Downtrend Start
//    2 = Downtrend Slowdn, -2 = Uptrend Slowdown
//    3 = Uptrend Pullback, -3 = Downtrend Pullback
//    4 = Move Stop Up,     -4 = Move Stop Down
//
//  gbKingOrderBlock Signal_Trade:
//    1 = Return Bullish,   -1 = Return Bearish
//    2 = Breakout Bullish, -2 = Breakout Bearish
// ============================================================

namespace NinjaTrader.NinjaScript.Indicators.GreyBeard.KingPanaZilla
{
[CategoryOrder("Developer",                  0)]
[CategoryOrder("General",                    1000010)]
[CategoryOrder("KingOrderBlock Parameters",  1000020)]
[CategoryOrder("PANAKanal Parameters",       1000030)]
[CategoryOrder("ThunderZilla Parameters",    1000040)]
[CategoryOrder("Visuals",                    1000050)]
[CategoryOrder("Logging",                    1000060)]
public class gbKingPanaZilla : Indicator
{
	// ---- child indicator references -------------------------
	private GreyBeard.KingPanaZilla.gbKingOrderBlock _king;
	private GreyBeard.KingPanaZilla.gbPANAKanal      _pana;
	private GreyBeard.KingPanaZilla.gbThunderZilla   _thunder;

	// ---- CSV logging ----------------------------------------
	private StreamWriter _logWriter;

	[Display(Name = "Version", Order = 0, GroupName = "Developer")]
	public string Version => "1.0.1";

	// ---- signal output series (Values[0..2]) ----------------
	// +1 = buy signal, -1 = sell signal, 0 = no signal
	[Browsable(false)]
	[XmlIgnore]
	public NinjaTrader.NinjaScript.Series<double> PanaZilla_Trade => Values[0];

	[Browsable(false)]
	[XmlIgnore]
	public NinjaTrader.NinjaScript.Series<double> KingZilla_Trade  => Values[1];

	[Browsable(false)]
	[XmlIgnore]
	public NinjaTrader.NinjaScript.Series<double> KingPana_Trade   => Values[2];

    public override string DisplayName
    {
        get { return " "; }
    }
    // =========================================================
        
	protected override void OnStateChange()
	{
		switch (State)
		{
		case State.SetDefaults:
			Description              = "Composite signal indicator â€” combines gbKingOrderBlock, gbPANAKanal, and gbThunderZilla into three cross-system trade signals (+1 buy / -1 sell).";
			Name                     = "gbKingPanaZilla";
			Calculate                = Calculate.OnBarClose;
			IsOverlay                = true;
			DisplayInDataBox         = true;
			DrawOnPricePanel         = true;
			PaintPriceMarkers        = false;
			ScaleJustification       = ScaleJustification.Right;
			IsSuspendedWhileInactive = false;
			BarsRequiredToPlot       = 0;
			ShowTransparentPlotsInDataBox = true;

			// ---- KingOrderBlock defaults --------------------
			King_SwingPointNeighborhood              = 5;
			King_ImbalanceQualifying                 = 3;
			King_OrderBlockFindingBosChochPeriod     = 50;
			King_OrderBlockAge                       = 500;
			King_OrderBlocksSameDirectionOffset      = 10;
			King_OrderBlocksDifferenceDirectionOffset= 10;
			King_SignalTradeQuantityPerOrderBlock     = 3;
			King_SignalTradeSplitBars                 = 6;

			// ---- PANAKanal defaults -------------------------
			Pana_Period                              = 20;
			Pana_Factor                              = 4.0;
			Pana_MiddlePeriod                        = 14;
			Pana_SignalBreakSplit                     = 20;
			Pana_SignalPullbackFindingPeriod          = 10;

			// ---- ThunderZilla defaults ----------------------
			Thunder_TrendMAType                      = gbThunderZillaMAType.SMA;
			Thunder_TrendPeriod                      = 100;
			Thunder_TrendSmoothingEnabled            = false;
			Thunder_TrendSmoothingMethod             = gbThunderZillaMAType.EMA;
			Thunder_TrendSmoothingPeriod             = 10;
			Thunder_StopOffsetMultiplierStop         = 60.0;
			Thunder_SignalQuantityPerFlat             = 2;
			Thunder_SignalQuantityPerTrend            = 999;

			// ---- Logging defaults --------------------------
			LogEnabled = false;

			// ---- Visual defaults ----------------------------
			PanaZilliaBrush = Brushes.Cyan;
			KingZillaBrush  = Brushes.DodgerBlue;
			KingPanaBrush   = Brushes.LimeGreen;
			ArrowOffset     = 3;

			// ---- Plots (must be in SetDefaults per NT8 requirement) ---------
			// Transparent plots — +1/−1/0 readable from DataBox and other scripts.
			// Visual arrows are drawn in OnBarUpdate via Draw.ArrowUp/Down.
			AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, “PanaZilla Trade”);
			AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, “KingZilla Trade”);
			AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, “KingPana Trade”);
			break;

		case State.Configure:
			break;

		case State.DataLoaded:
			// Close any existing log writer before creating a new one (handles replay reset)
			if (_logWriter != null)
			{
				_logWriter.Flush();
				_logWriter.Dispose();
				_logWriter = null;
			}

			// Factory methods (CacheIndicator) add child indicators to NinjaScripts so
			// their OnBarUpdate runs automatically. AddChartIndicator is Strategy-only;
			// add the three child indicators directly to the chart if visual rendering
			// of their zones/stops is also needed.
			_king = gbKingOrderBlock(
				King_SwingPointNeighborhood,
				King_ImbalanceQualifying,
				King_OrderBlockFindingBosChochPeriod,
				King_OrderBlockAge,
				King_OrderBlocksSameDirectionOffset,
				King_OrderBlocksDifferenceDirectionOffset,
				King_SignalTradeQuantityPerOrderBlock,
				King_SignalTradeSplitBars);

			_pana = gbPANAKanal(
				Pana_Period,
				Pana_Factor,
				Pana_MiddlePeriod,
				Pana_SignalBreakSplit,
				Pana_SignalPullbackFindingPeriod);

			_thunder = gbThunderZilla(
				Thunder_TrendMAType,
				Thunder_TrendPeriod,
				Thunder_TrendSmoothingEnabled,
				Thunder_TrendSmoothingMethod,
				Thunder_TrendSmoothingPeriod,
				Thunder_StopOffsetMultiplierStop,
				Thunder_SignalQuantityPerFlat,
				Thunder_SignalQuantityPerTrend);

			if (LogEnabled)
			{
				string logPath = Path.Combine(
					Globals.UserDataDir,
					"gbKPZlog_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
				_logWriter = new StreamWriter(logPath, append: false, encoding: Encoding.UTF8);
				_logWriter.WriteLine("DateTime,Instrument,Price,PanaZilla_Trade,KingZilla_Trade,KingPana_Trade");
				_logWriter.Flush();
			}
			break;

		case State.Terminated:
			_king    = null;
			_pana    = null;
			_thunder = null;
			if (_logWriter != null)
			{
				_logWriter.Flush();
				_logWriter.Dispose();
				_logWriter = null;
			}
			break;
		}
	}

	// =========================================================
	protected override void OnBarUpdate()
	{
		if (_pana == null || _thunder == null || _king == null)
			return;

		// Reset all signal plots each bar
		Values[0][0] = Values[1][0] = Values[2][0] = 0;

		double pkSig  = _pana.Signal_Trade[0];
		double tzSig  = _thunder.Signal_Trade[0];
		double koSig  = _king.Signal_Trade[0];
		double offset = ArrowOffset * TickSize;

		// ---- PanaZillia Trade ----
		if (pkSig >= 2 && tzSig >= 3)
		{
			Values[0][0] = 1;
			Draw.ArrowUp(this, "KPZ_PZ_" + CurrentBar, false,
				0, Low[0] - offset, PanaZilliaBrush);
		}
		else if (pkSig <= -2 && tzSig <= -3)
		{
			Values[0][0] = -1;
			Draw.ArrowDown(this, "KPZ_PZ_" + CurrentBar, false,
				0, High[0] + offset, PanaZilliaBrush);
		}

		// ---- KingZilla Trade ----
		if (tzSig >= 3 && koSig >= 1)
		{
			Values[1][0] = 1;
			Draw.ArrowUp(this, "KPZ_KZ_" + CurrentBar, false,
				0, Low[0] - offset, KingZillaBrush);
		}
		else if (tzSig <= -3 && koSig <= -1)
		{
			Values[1][0] = -1;
			Draw.ArrowDown(this, "KPZ_KZ_" + CurrentBar, false,
				0, High[0] + offset, KingZillaBrush);
		}

		// ---- KingPana Trade ----
		if (pkSig >= 2 && koSig >= 1)
		{
			Values[2][0] = 1;
			Draw.ArrowUp(this, "KPZ_KP_" + CurrentBar, false,
				0, Low[0] - offset, KingPanaBrush);
		}
		else if (pkSig <= -2 && koSig <= -1)
		{
			Values[2][0] = -1;
			Draw.ArrowDown(this, "KPZ_KP_" + CurrentBar, false,
				0, High[0] + offset, KingPanaBrush);
		}

		// ---- CSV log (write when any signal fires) -----------
		if (_logWriter != null)
		{
			int pzVal = (int)Values[0][0];
			int kzVal = (int)Values[1][0];
			int kpVal = (int)Values[2][0];
			if (pzVal != 0 || kzVal != 0 || kpVal != 0)
			{
				_logWriter.WriteLine(string.Format("{0},\"{1}\",{2},{3},{4},{5}",
					Time[0].ToString("yyyy-MM-dd HH:mm:ss"),
					Instrument.FullName,
					Close[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
					pzVal, kzVal, kpVal));
				_logWriter.Flush();
			}
		}
	}

	// =========================================================
	#region Properties

	[Display(Name = "Author",  Order = 0, GroupName = "Developer")]
	public string Author  => "GreyBeard";

	[Display(Name = "Version", Order = 1, GroupName = "Developer")]
	public string Version => "1.0";

	// ---- KingOrderBlock parameters --------------------------
	[Display(Name = "Swing Point: Neighborhood", Order = 0, GroupName = "KingOrderBlock Parameters")]
	[Range(1, int.MaxValue)]
	public int King_SwingPointNeighborhood { get; set; }

	[Display(Name = "Imbalance: Qualifying (Bars)", Order = 10, GroupName = "KingOrderBlock Parameters")]
	[Range(1, int.MaxValue)]
	public int King_ImbalanceQualifying { get; set; }

	[Display(Name = "Order Block: Finding BOS/CHoCH Period", Order = 20, GroupName = "KingOrderBlock Parameters")]
	[Range(1, int.MaxValue)]
	public int King_OrderBlockFindingBosChochPeriod { get; set; }

	[Display(Name = "Order Block: Age (Bars)", Order = 30, GroupName = "KingOrderBlock Parameters")]
	[Range(0, int.MaxValue)]
	public int King_OrderBlockAge { get; set; }

	[Display(Name = "Order Blocks: Same Direction Offset (Ticks)", Order = 40, GroupName = "KingOrderBlock Parameters")]
	[Range(0, int.MaxValue)]
	public int King_OrderBlocksSameDirectionOffset { get; set; }

	[Display(Name = "Order Blocks: Diff Direction Offset (Ticks)", Order = 50, GroupName = "KingOrderBlock Parameters")]
	[Range(0, int.MaxValue)]
	public int King_OrderBlocksDifferenceDirectionOffset { get; set; }

	[Display(Name = "Signal Trade: Quantity Per OB", Order = 60, GroupName = "KingOrderBlock Parameters")]
	[Range(1, int.MaxValue)]
	public int King_SignalTradeQuantityPerOrderBlock { get; set; }

	[Display(Name = "Signal Trade: Split (Bars)", Order = 70, GroupName = "KingOrderBlock Parameters")]
	[Range(1, int.MaxValue)]
	public int King_SignalTradeSplitBars { get; set; }

	// ---- PANAKanal parameters --------------------------------
	[Display(Name = "Period", Order = 0, GroupName = "PANAKanal Parameters")]
	[Range(1, int.MaxValue)]
	public int Pana_Period { get; set; }

	[Display(Name = "Factor", Order = 10, GroupName = "PANAKanal Parameters")]
	[Range(0.01, double.MaxValue)]
	public double Pana_Factor { get; set; }

	[Display(Name = "Middle Period", Order = 20, GroupName = "PANAKanal Parameters")]
	[Range(1, int.MaxValue)]
	public int Pana_MiddlePeriod { get; set; }

	[Display(Name = "Signal Break Split (Bars)", Order = 30, GroupName = "PANAKanal Parameters")]
	[Range(1, int.MaxValue)]
	public int Pana_SignalBreakSplit { get; set; }

	[Display(Name = "Signal Pullback Finding Period", Order = 40, GroupName = "PANAKanal Parameters")]
	[Range(1, int.MaxValue)]
	public int Pana_SignalPullbackFindingPeriod { get; set; }

	// ---- ThunderZilla parameters ----------------------------
	[Display(Name = "Trend: MA Type", Order = 0, GroupName = "ThunderZilla Parameters")]
	public gbThunderZillaMAType Thunder_TrendMAType { get; set; }

	[Display(Name = "Trend: Period", Order = 10, GroupName = "ThunderZilla Parameters")]
	[Range(1, int.MaxValue)]
	public int Thunder_TrendPeriod { get; set; }

	[Display(Name = "Trend: Smoothing Enabled", Order = 20, GroupName = "ThunderZilla Parameters")]
	public bool Thunder_TrendSmoothingEnabled { get; set; }

	[Display(Name = "Trend: Smoothing Method", Order = 30, GroupName = "ThunderZilla Parameters")]
	public gbThunderZillaMAType Thunder_TrendSmoothingMethod { get; set; }

	[Display(Name = "Trend: Smoothing Period", Order = 40, GroupName = "ThunderZilla Parameters")]
	[Range(1, int.MaxValue)]
	public int Thunder_TrendSmoothingPeriod { get; set; }

	[Display(Name = "Stop: Offset Multiplier (Ticks)", Order = 50, GroupName = "ThunderZilla Parameters")]
	[Range(0.0, double.MaxValue)]
	public double Thunder_StopOffsetMultiplierStop { get; set; }

	[Display(Name = "Signal: Quantity Per Flat", Order = 60, GroupName = "ThunderZilla Parameters")]
	[Range(1, int.MaxValue)]
	public int Thunder_SignalQuantityPerFlat { get; set; }

	[Display(Name = "Signal: Quantity Per Trend", Order = 70, GroupName = "ThunderZilla Parameters")]
	[Range(1, int.MaxValue)]
	public int Thunder_SignalQuantityPerTrend { get; set; }

	// ---- Visual parameters -----------------------------------
	[Display(Name = "PanaZillia Color", Order = 0, GroupName = "Visuals")]
	[XmlIgnore]
	public Brush PanaZilliaBrush { get; set; }
	[Browsable(false)]
	public string PanaZilliaBrushSerialize
	{
		get { return Serialize.BrushToString(PanaZilliaBrush); }
		set { PanaZilliaBrush = Serialize.StringToBrush(value); }
	}

	[Display(Name = "KingZilla Color", Order = 1, GroupName = "Visuals")]
	[XmlIgnore]
	public Brush KingZillaBrush { get; set; }
	[Browsable(false)]
	public string KingZillaBrushSerialize
	{
		get { return Serialize.BrushToString(KingZillaBrush); }
		set { KingZillaBrush = Serialize.StringToBrush(value); }
	}

	[Display(Name = "KingPana Color", Order = 2, GroupName = "Visuals")]
	[XmlIgnore]
	public Brush KingPanaBrush { get; set; }
	[Browsable(false)]
	public string KingPanaBrushSerialize
	{
		get { return Serialize.BrushToString(KingPanaBrush); }
		set { KingPanaBrush = Serialize.StringToBrush(value); }
	}

	[Display(Name = "Arrow Offset (Ticks)", Order = 3, GroupName = "Visuals")]
	[Range(0, int.MaxValue)]
	public int ArrowOffset { get; set; }

	// ---- Logging parameters ---------------------------------
	[Display(Name = "Enabled", Order = 0, GroupName = "Logging",
		Description = "Write a CSV signal log to the NinjaTrader user data folder "
		            + "(Documents\\NinjaTrader 8\\). File: gbKPZlog_YYYYMMDD_HHmmss.csv. "
		            + "One row per bar where at least one trade signal fires.")]
	public bool LogEnabled { get; set; }

	#endregion

} // class gbKingPanaZilla

} // namespace NinjaTrader.NinjaScript.Indicators.GreyBeard.KingPanaZilla

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GreyBeard.KingPanaZilla.gbKingPanaZilla[] cachegbKingPanaZilla;
		public GreyBeard.KingPanaZilla.gbKingPanaZilla gbKingPanaZilla()
		{
			return gbKingPanaZilla(Input);
		}

		public GreyBeard.KingPanaZilla.gbKingPanaZilla gbKingPanaZilla(ISeries<double> input)
		{
			if (cachegbKingPanaZilla != null)
				for (int idx = 0; idx < cachegbKingPanaZilla.Length; idx++)
					if (cachegbKingPanaZilla[idx] != null &&  cachegbKingPanaZilla[idx].EqualsInput(input))
						return cachegbKingPanaZilla[idx];
			return CacheIndicator<GreyBeard.KingPanaZilla.gbKingPanaZilla>(new GreyBeard.KingPanaZilla.gbKingPanaZilla(), input, ref cachegbKingPanaZilla);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GreyBeard.KingPanaZilla.gbKingPanaZilla gbKingPanaZilla()
		{
			return indicator.gbKingPanaZilla(Input);
		}

		public Indicators.GreyBeard.KingPanaZilla.gbKingPanaZilla gbKingPanaZilla(ISeries<double> input )
		{
			return indicator.gbKingPanaZilla(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GreyBeard.KingPanaZilla.gbKingPanaZilla gbKingPanaZilla()
		{
			return indicator.gbKingPanaZilla(Input);
		}

		public Indicators.GreyBeard.KingPanaZilla.gbKingPanaZilla gbKingPanaZilla(ISeries<double> input )
		{
			return indicator.gbKingPanaZilla(input);
		}
	}
}

#endregion
