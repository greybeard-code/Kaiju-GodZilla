#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// ============================================================
//  gbSumoPanaZilla
//  Combines gbThunderZilla + gbPANAKanal + RenkoKings_SumoPullback
//
//  THREE output signal plots (strict same-bar confirmation):
//
//  PK_TZ_SP
//    BUY:  PanaKanal >= 2  AND  ThunderZilla >= 3  (same bar)
//    SELL: PanaKanal <= -2 AND  ThunderZilla <= -3 (same bar)
//
//  TZ_Sumo_SP
//    BUY:  ThunderZilla >= 3  AND  Sumo >= 1       (same bar)
//    SELL: ThunderZilla <= -3 AND  Sumo <= -1      (same bar)
//
//  PK_Sumo_SP
//    BUY:  PanaKanal >= 2  AND  Sumo >= 1          (same bar)
//    SELL: PanaKanal <= -2 AND  Sumo <= -1         (same bar)
//
//  Plot values: +1 = buy, -1 = sell, 0 = no signal
// ============================================================

namespace NinjaTrader.NinjaScript.Indicators.GreyBeard
{
[CategoryOrder("Developer",           0)]
[CategoryOrder("1. ThunderZilla",    1000010)]
[CategoryOrder("2. PANA Kanal",      1000020)]
[CategoryOrder("3. Sumo Pullback",   1000030)]
[CategoryOrder("4. Visuals",         1000040)]
public class gbSumoPanaZilla : Indicator
{
    private GreyBeard.KingPanaZilla.gbPANAKanal                           _pana;
    private GreyBeard.KingPanaZilla.gbThunderZilla                        _thunder;
    private Indicators.RenkoKings.RenkoKings_SumoPullback                 _sumo;

    private const int PLOT_PK_TZ   = 0;
    private const int PLOT_TZ_SUMO = 1;
    private const int PLOT_PK_SUMO = 2;

    public override string DisplayName => " ";

    protected override void OnStateChange()
    {
        switch (State)
        {
        case State.SetDefaults:
            Description              = "Composite signal indicator — combines gbThunderZilla, gbPANAKanal, and RenkoKings_SumoPullback into three cross-system trade signals (+1 buy / -1 sell).";
            Name                     = "gbSumoPanaZilla";
            Calculate                = Calculate.OnBarClose;
            IsOverlay                = true;
            DisplayInDataBox         = true;
            DrawOnPricePanel         = true;
            PaintPriceMarkers        = false;
            ScaleJustification       = ScaleJustification.Right;
            IsSuspendedWhileInactive = true;
            BarsRequiredToPlot       = 0;
            ShowTransparentPlotsInDataBox = true;

            // ---- ThunderZilla defaults ----------------------
            Thunder_TrendMAType              = gbThunderZillaMAType.SMA;
            Thunder_TrendPeriod              = 100;
            Thunder_TrendSmoothingEnabled    = false;
            Thunder_TrendSmoothingMethod     = gbThunderZillaMAType.EMA;
            Thunder_TrendSmoothingPeriod     = 10;
            Thunder_StopOffsetMultiplier     = 60.0;
            Thunder_SignalQtyPerFlat         = 2;
            Thunder_SignalQtyPerTrend        = 999;

            // ---- PANA Kanal defaults -------------------------
            Pana_Period                      = 20;
            Pana_Factor                      = 4.0;
            Pana_MiddlePeriod                = 14;
            Pana_SignalBreakSplit            = 20;
            Pana_SignalPullbackFindingPeriod = 10;

            // ---- Sumo Pullback defaults ----------------------
            SP_SlowMAType                    = ninZa_MAType.SMA;
            SP_SlowMAPeriod                  = 200;
            SP_SlowMASmoothingEnabled        = false;
            SP_SlowMASmoothingMethod         = ninZa_MAType.EMA;
            SP_SlowMASmoothingPeriod         = 10;
            SP_FastMA1Type                   = ninZa_MAType.EMA;
            SP_FastMA1Period                 = 10;
            SP_FastMA1SmoothingEnabled       = false;
            SP_FastMA1SmoothingMethod        = ninZa_MAType.EMA;
            SP_FastMA1SmoothingPeriod        = 10;
            SP_FastMA2Type                   = ninZa_MAType.EMA;
            SP_FastMA2Period                 = 21;
            SP_FastMA2SmoothingEnabled       = false;
            SP_FastMA2SmoothingMethod        = ninZa_MAType.EMA;
            SP_FastMA2SmoothingPeriod        = 10;
            SP_FastMA3Type                   = ninZa_MAType.EMA;
            SP_FastMA3Period                 = 50;
            SP_FastMA3SmoothingEnabled       = false;
            SP_FastMA3SmoothingMethod        = ninZa_MAType.EMA;
            SP_FastMA3SmoothingPeriod        = 10;
            SP_SignalSplitFirst              = 3;
            SP_SignalSplitSecond             = 50;

            // ---- Visual defaults ----------------------------
            PK_TZ_Brush   = Brushes.Cyan;
            TZ_Sumo_Brush = Brushes.Lime;
            PK_Sumo_Brush = Brushes.Yellow;
            ArrowOffset   = 3;
            break;

        case State.Configure:
            AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "PK_TZ_SP");
            AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "TZ_Sumo_SP");
            AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "PK_Sumo_SP");
            break;

        case State.DataLoaded:
            _thunder = gbThunderZilla(
                Thunder_TrendMAType,
                Thunder_TrendPeriod,
                Thunder_TrendSmoothingEnabled,
                Thunder_TrendSmoothingMethod,
                Thunder_TrendSmoothingPeriod,
                Thunder_StopOffsetMultiplier,
                Thunder_SignalQtyPerFlat,
                Thunder_SignalQtyPerTrend);

            _pana = gbPANAKanal(
                Pana_Period,
                Pana_Factor,
                Pana_MiddlePeriod,
                Pana_SignalBreakSplit,
                Pana_SignalPullbackFindingPeriod);

            _sumo = RenkoKings_SumoPullback(
                SP_SlowMAType,
                SP_SlowMAPeriod,
                SP_SlowMASmoothingEnabled,
                SP_SlowMASmoothingMethod,
                SP_SlowMASmoothingPeriod,
                SP_FastMA1Type,
                SP_FastMA1Period,
                SP_FastMA1SmoothingEnabled,
                SP_FastMA1SmoothingMethod,
                SP_FastMA1SmoothingPeriod,
                SP_FastMA2Type,
                SP_FastMA2Period,
                SP_FastMA2SmoothingEnabled,
                SP_FastMA2SmoothingMethod,
                SP_FastMA2SmoothingPeriod,
                SP_FastMA3Type,
                SP_FastMA3Period,
                SP_FastMA3SmoothingEnabled,
                SP_FastMA3SmoothingMethod,
                SP_FastMA3SmoothingPeriod,
                SP_SignalSplitFirst,
                SP_SignalSplitSecond);
            break;

        case State.Terminated:
            _pana    = null;
            _thunder = null;
            _sumo    = null;
            break;
        }
    }

    protected override void OnBarUpdate()
    {
        if (_pana == null || _thunder == null || _sumo == null)
            return;

        Values[PLOT_PK_TZ][0]   = 0;
        Values[PLOT_TZ_SUMO][0] = 0;
        Values[PLOT_PK_SUMO][0] = 0;

        double pkSig  = _pana.Signal_Trade[0];
        double tzSig  = _thunder.Signal_Trade[0];
        double spSig  = _sumo.Signal_Trade[0];
        double offset = ArrowOffset * TickSize;

        // ---- PK_TZ_SP ----
        if (pkSig >= 2 && tzSig >= 3)
        {
            Values[PLOT_PK_TZ][0] = 1;
            Draw.ArrowUp(this, "SPZ_PKTZ_" + CurrentBar, false, 0,
                Low[0] - offset, PK_TZ_Brush);
        }
        else if (pkSig <= -2 && tzSig <= -3)
        {
            Values[PLOT_PK_TZ][0] = -1;
            Draw.ArrowDown(this, "SPZ_PKTZ_" + CurrentBar, false, 0,
                High[0] + offset, PK_TZ_Brush);
        }

        // ---- TZ_Sumo_SP ----
        if (tzSig >= 3 && spSig >= 1)
        {
            Values[PLOT_TZ_SUMO][0] = 1;
            Draw.ArrowUp(this, "SPZ_TZSumo_" + CurrentBar, false, 0,
                Low[0] - (offset * 2), TZ_Sumo_Brush);
        }
        else if (tzSig <= -3 && spSig <= -1)
        {
            Values[PLOT_TZ_SUMO][0] = -1;
            Draw.ArrowDown(this, "SPZ_TZSumo_" + CurrentBar, false, 0,
                High[0] + (offset * 2), TZ_Sumo_Brush);
        }

        // ---- PK_Sumo_SP ----
        if (pkSig >= 2 && spSig >= 1)
        {
            Values[PLOT_PK_SUMO][0] = 1;
            Draw.ArrowUp(this, "SPZ_PKSumo_" + CurrentBar, false, 0,
                Low[0] - (offset * 3), PK_Sumo_Brush);
        }
        else if (pkSig <= -2 && spSig <= -1)
        {
            Values[PLOT_PK_SUMO][0] = -1;
            Draw.ArrowDown(this, "SPZ_PKSumo_" + CurrentBar, false, 0,
                High[0] + (offset * 3), PK_Sumo_Brush);
        }
    }

    #region Properties

    [Display(Name = "Author",  Order = 0, GroupName = "Developer")]
    public string Author  => "GreyBeard";

    [Display(Name = "Version", Order = 1, GroupName = "Developer")]
    public string Version => "1.0";

    // =====================================================================
    // GROUP 1: ThunderZilla Parameters
    // =====================================================================

    [Display(Name = "Trend: MA Type", Order = 0, GroupName = "1. ThunderZilla")]
    public gbThunderZillaMAType Thunder_TrendMAType { get; set; }

    [Display(Name = "Trend: Period", Order = 10, GroupName = "1. ThunderZilla")]
    [Range(1, int.MaxValue)]
    public int Thunder_TrendPeriod { get; set; }

    [Display(Name = "Trend: Smoothing Enabled", Order = 20, GroupName = "1. ThunderZilla")]
    public bool Thunder_TrendSmoothingEnabled { get; set; }

    [Display(Name = "Trend: Smoothing Method", Order = 30, GroupName = "1. ThunderZilla")]
    public gbThunderZillaMAType Thunder_TrendSmoothingMethod { get; set; }

    [Display(Name = "Trend: Smoothing Period", Order = 40, GroupName = "1. ThunderZilla")]
    [Range(1, int.MaxValue)]
    public int Thunder_TrendSmoothingPeriod { get; set; }

    [Display(Name = "Stop: Offset Multiplier (Ticks)", Order = 50, GroupName = "1. ThunderZilla")]
    [Range(0.0, double.MaxValue)]
    public double Thunder_StopOffsetMultiplier { get; set; }

    [Display(Name = "Signal: Qty Per Flat", Order = 60, GroupName = "1. ThunderZilla")]
    [Range(1, int.MaxValue)]
    public int Thunder_SignalQtyPerFlat { get; set; }

    [Display(Name = "Signal: Qty Per Trend", Order = 70, GroupName = "1. ThunderZilla")]
    [Range(1, int.MaxValue)]
    public int Thunder_SignalQtyPerTrend { get; set; }

    // =====================================================================
    // GROUP 2: PANA Kanal Parameters
    // =====================================================================

    [Display(Name = "Period", Order = 0, GroupName = "2. PANA Kanal")]
    [Range(1, int.MaxValue)]
    public int Pana_Period { get; set; }

    [Display(Name = "Factor", Order = 10, GroupName = "2. PANA Kanal")]
    [Range(0.01, double.MaxValue)]
    public double Pana_Factor { get; set; }

    [Display(Name = "Middle Period", Order = 20, GroupName = "2. PANA Kanal")]
    [Range(1, int.MaxValue)]
    public int Pana_MiddlePeriod { get; set; }

    [Display(Name = "Signal Break Split (Bars)", Order = 30, GroupName = "2. PANA Kanal")]
    [Range(1, int.MaxValue)]
    public int Pana_SignalBreakSplit { get; set; }

    [Display(Name = "Signal Pullback Finding Period", Order = 40, GroupName = "2. PANA Kanal")]
    [Range(1, int.MaxValue)]
    public int Pana_SignalPullbackFindingPeriod { get; set; }

    // =====================================================================
    // GROUP 3: Sumo Pullback Parameters
    // =====================================================================

    [Display(Name = "Slow MA Type", Order = 0, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_SlowMAType { get; set; }

    [Display(Name = "Slow MA Period", Order = 10, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_SlowMAPeriod { get; set; }

    [Display(Name = "Slow MA Smoothing Enabled", Order = 20, GroupName = "3. Sumo Pullback")]
    public bool SP_SlowMASmoothingEnabled { get; set; }

    [Display(Name = "Slow MA Smoothing Method", Order = 30, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_SlowMASmoothingMethod { get; set; }

    [Display(Name = "Slow MA Smoothing Period", Order = 40, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_SlowMASmoothingPeriod { get; set; }

    [Display(Name = "Fast MA1 Type", Order = 50, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_FastMA1Type { get; set; }

    [Display(Name = "Fast MA1 Period", Order = 60, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_FastMA1Period { get; set; }

    [Display(Name = "Fast MA1 Smoothing Enabled", Order = 70, GroupName = "3. Sumo Pullback")]
    public bool SP_FastMA1SmoothingEnabled { get; set; }

    [Display(Name = "Fast MA1 Smoothing Method", Order = 80, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_FastMA1SmoothingMethod { get; set; }

    [Display(Name = "Fast MA1 Smoothing Period", Order = 90, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_FastMA1SmoothingPeriod { get; set; }

    [Display(Name = "Fast MA2 Type", Order = 100, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_FastMA2Type { get; set; }

    [Display(Name = "Fast MA2 Period", Order = 110, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_FastMA2Period { get; set; }

    [Display(Name = "Fast MA2 Smoothing Enabled", Order = 120, GroupName = "3. Sumo Pullback")]
    public bool SP_FastMA2SmoothingEnabled { get; set; }

    [Display(Name = "Fast MA2 Smoothing Method", Order = 130, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_FastMA2SmoothingMethod { get; set; }

    [Display(Name = "Fast MA2 Smoothing Period", Order = 140, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_FastMA2SmoothingPeriod { get; set; }

    [Display(Name = "Fast MA3 Type", Order = 150, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_FastMA3Type { get; set; }

    [Display(Name = "Fast MA3 Period", Order = 160, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_FastMA3Period { get; set; }

    [Display(Name = "Fast MA3 Smoothing Enabled", Order = 170, GroupName = "3. Sumo Pullback")]
    public bool SP_FastMA3SmoothingEnabled { get; set; }

    [Display(Name = "Fast MA3 Smoothing Method", Order = 180, GroupName = "3. Sumo Pullback")]
    public ninZa_MAType SP_FastMA3SmoothingMethod { get; set; }

    [Display(Name = "Fast MA3 Smoothing Period", Order = 190, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_FastMA3SmoothingPeriod { get; set; }

    [Display(Name = "Signal Split First", Order = 200, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_SignalSplitFirst { get; set; }

    [Display(Name = "Signal Split Second", Order = 210, GroupName = "3. Sumo Pullback")]
    [Range(1, int.MaxValue)]
    public int SP_SignalSplitSecond { get; set; }

    // =====================================================================
    // GROUP 4: Visuals
    // =====================================================================

    [Display(Name = "PK-TZ-SP Color", Order = 0, GroupName = "4. Visuals")]
    [XmlIgnore]
    public Brush PK_TZ_Brush { get; set; }
    [Browsable(false)]
    public string PK_TZ_BrushSerialize
    {
        get { return Serialize.BrushToString(PK_TZ_Brush); }
        set { PK_TZ_Brush = Serialize.StringToBrush(value); }
    }

    [Display(Name = "TZ-Sumo-SP Color", Order = 10, GroupName = "4. Visuals")]
    [XmlIgnore]
    public Brush TZ_Sumo_Brush { get; set; }
    [Browsable(false)]
    public string TZ_Sumo_BrushSerialize
    {
        get { return Serialize.BrushToString(TZ_Sumo_Brush); }
        set { TZ_Sumo_Brush = Serialize.StringToBrush(value); }
    }

    [Display(Name = "PK-Sumo-SP Color", Order = 20, GroupName = "4. Visuals")]
    [XmlIgnore]
    public Brush PK_Sumo_Brush { get; set; }
    [Browsable(false)]
    public string PK_Sumo_BrushSerialize
    {
        get { return Serialize.BrushToString(PK_Sumo_Brush); }
        set { PK_Sumo_Brush = Serialize.StringToBrush(value); }
    }

    [Display(Name = "Arrow Offset (Ticks)", Order = 30, GroupName = "4. Visuals",
        Description = "Base tick offset. PK-TZ-SP=1x, TZ-Sumo-SP=2x, PK-Sumo-SP=3x to stack arrows vertically.")]
    [Range(0, int.MaxValue)]
    public int ArrowOffset { get; set; }

    // =====================================================================
    // Output plots
    // =====================================================================

    [Browsable(false)]
    [XmlIgnore]
    [Display(Name = "PK-TZ-SP Trade Signal")]
    public Series<double> PK_TZ_SP { get { return Values[PLOT_PK_TZ]; } }

    [Browsable(false)]
    [XmlIgnore]
    [Display(Name = "TZ-Sumo-SP Trade Signal")]
    public Series<double> TZ_Sumo_SP { get { return Values[PLOT_TZ_SUMO]; } }

    [Browsable(false)]
    [XmlIgnore]
    [Display(Name = "PK-Sumo-SP Trade Signal")]
    public Series<double> PK_Sumo_SP { get { return Values[PLOT_PK_SUMO]; } }

    #endregion

} // class gbSumoPanaZilla

} // namespace NinjaTrader.NinjaScript.Indicators.GreyBeard

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GreyBeard.gbSumoPanaZilla[] cachegbSumoPanaZilla;

		public GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return gbSumoPanaZilla(Input);
		}

		public GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input)
		{
			if (cachegbSumoPanaZilla != null)
				for (int idx = 0; idx < cachegbSumoPanaZilla.Length; idx++)
					if (cachegbSumoPanaZilla[idx] != null && cachegbSumoPanaZilla[idx].EqualsInput(input))
						return cachegbSumoPanaZilla[idx];
			return CacheIndicator<GreyBeard.gbSumoPanaZilla>(new GreyBeard.gbSumoPanaZilla(){}, input, ref cachegbSumoPanaZilla);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return indicator.gbSumoPanaZilla(Input);
		}

		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input)
		{
			return indicator.gbSumoPanaZilla(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return indicator.gbSumoPanaZilla(Input);
		}

		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input)
		{
			return indicator.gbSumoPanaZilla(input);
		}
	}
}

#endregion

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GreyBeard.gbSumoPanaZilla[] cachegbSumoPanaZilla;
		public GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return gbSumoPanaZilla(Input);
		}

		public GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input)
		{
			if (cachegbSumoPanaZilla != null)
				for (int idx = 0; idx < cachegbSumoPanaZilla.Length; idx++)
					if (cachegbSumoPanaZilla[idx] != null &&  cachegbSumoPanaZilla[idx].EqualsInput(input))
						return cachegbSumoPanaZilla[idx];
			return CacheIndicator<GreyBeard.gbSumoPanaZilla>(new GreyBeard.gbSumoPanaZilla(), input, ref cachegbSumoPanaZilla);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return indicator.gbSumoPanaZilla(Input);
		}

		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input )
		{
			return indicator.gbSumoPanaZilla(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return indicator.gbSumoPanaZilla(Input);
		}

		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input )
		{
			return indicator.gbSumoPanaZilla(input);
		}
	}
}

#endregion

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GreyBeard.gbSumoPanaZilla[] cachegbSumoPanaZilla;
		public GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return gbSumoPanaZilla(Input);
		}

		public GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input)
		{
			if (cachegbSumoPanaZilla != null)
				for (int idx = 0; idx < cachegbSumoPanaZilla.Length; idx++)
					if (cachegbSumoPanaZilla[idx] != null &&  cachegbSumoPanaZilla[idx].EqualsInput(input))
						return cachegbSumoPanaZilla[idx];
			return CacheIndicator<GreyBeard.gbSumoPanaZilla>(new GreyBeard.gbSumoPanaZilla(), input, ref cachegbSumoPanaZilla);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return indicator.gbSumoPanaZilla(Input);
		}

		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input )
		{
			return indicator.gbSumoPanaZilla(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla()
		{
			return indicator.gbSumoPanaZilla(Input);
		}

		public Indicators.GreyBeard.gbSumoPanaZilla gbSumoPanaZilla(ISeries<double> input )
		{
			return indicator.gbSumoPanaZilla(input);
		}
	}
}

#endregion
