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
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Xml.Serialization;
using NewsPrintLocation = NinjaTrader.NinjaScript.Indicators.Playr101.NewsSignals.NewsPrintLocation;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.Playr101
{
    #region GUI Categories
    [CategoryOrder ("Display", 0)]
    [CategoryOrder ("News Filter", 1)]
    [CategoryOrder ("Strategy Blocking", 2)]
    [CategoryOrder ("Alerts", 3)]
    [CategoryOrder ("Colors", 4)]
    [CategoryOrder ("Fonts", 5)]
    [CategoryOrder ("Debug", 6)]
    #endregion

    public class NewsSignals : Indicator
    {
        public enum NewsPrintLocation
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Custom
        }

        private List<TextLine> list;

        private class TextColumn
        {
            public TextColumn (float padding, string text)
            {
                this.padding = padding;
                this.text = text;
            }

            public float padding;
            public string text;
        }

        private class TextLine
        {
            public TextLine (SimpleFont font, System.Windows.Media.Brush brush)
            {
                this.font = font;
                this.brush = brush;
            }

            public TextColumn timeColumn;
            public TextColumn impactColumn;
            public TextColumn descColumn;
            public SimpleFont font;
            public System.Windows.Media.Brush brush;
        }

        public class NewsEvent
        {
            public int ID;
            public string Title;
            public string Country;
            public string Date;
            public string Time;
            public string Impact;
            public string Forecast;
            public string Previous;

            [XmlIgnore()]
            public DateTime DateTimeLocal;

            public override string ToString ()
            {
                return string.Format ("ID: {0}, Title: {1}, Country: {2}, Date: {3}, Time: {4}, Impact: {5}, Forecast: {6}, Previous: {7}, DateTimeLocal: {8}",
                    ID, Title, Country, Date, Time, Impact, Forecast, Previous, DateTimeLocal);
            }
        }

        private const string ffNewsUrl = @"http://nfs.faireconomy.media/ff_calendar_thisweek.xml";
        private const string TIME = "Time";
        private const string IMPACT = "Impact";
        private const string DESC = "News Event Description (prev/forecast)";
        private const float TIME_PAD = 10;
        private const float IMPACT_PAD = 10;
        private const float DESC_PAD = 0;

        private NewsEvent[] newsEvents = null;

        private DateTime lastNewsUpdate = DateTime.MinValue;
        private string lastLoadError;

        private float widestTimeCol = 0;
        private float widestImpactCol = 0;
        private float widestDescCol = 0;
        private float totalHeight = 0;
        private float longestLine = 0;
        private int lastNewsPtr = 0;
        private DateTime lastMinute;

        private CultureInfo ffDateTimeCulture = CultureInfo.CreateSpecificCulture ("en-US");

        private int newsItemPtr = 0;

        private SimpleFont titleFont = new SimpleFont ("Arial", 10) { Bold = true };
        private SimpleFont defaultFont = new SimpleFont ("Arial", 10) { };
        private SimpleFont lineAlertFont = new SimpleFont ("Arial", 10) { Bold = true, Italic = true };

        private System.Windows.Media.Brush headerColor = Brushes.White;
        private System.Windows.Media.Brush defaultTextColor = Brushes.White;
        private System.Windows.Media.Brush warningTextColor = Brushes.Yellow;
        private System.Windows.Media.Brush lineHighColor = Brushes.Red;
        private System.Windows.Media.Brush lineMedColor = Brushes.DarkGreen;
        private System.Windows.Media.Brush lineLowColor = Brushes.Blue;
        private System.Windows.Media.Brush backgroundColor = new System.Windows.Media.SolidColorBrush (System.Windows.Media.Color.FromArgb (170, 0, 0, 0));
        private System.Windows.Media.Brush newsTimeBackBrush = Brushes.LightYellow;

        private System.Windows.Media.Brush headerTitleBrush;
        private System.Windows.Media.Brush defaultTextBrush;
        private System.Windows.Media.Brush warningTextBrush;
        private System.Windows.Media.Brush lineHighBrush;
        private System.Windows.Media.Brush lineMedBrush;
        private System.Windows.Media.Brush lineLowBrush;
        private System.Windows.Media.Brush lineNormalBrush;

        private HashSet<string> alertedEventKeys = new HashSet<string> ();

        private bool newsBlockActive = false;
        private double minutesToNextNews = double.NaN;
        private double nextImpactScore = 0.0;
        private double minutesFromRecentNews = double.NaN;
        private string nextNewsTitle = string.Empty;
        private DateTime nextNewsTime = Core.Globals.MinDate;

        [Display (Name = "Version", Order = 0, GroupName = "Debug")]
        public string Version => "1.0.1";

        public override string DisplayName
        {
            get
            {
                return this.Name.ToString ();
            }
        }

        protected override void OnStateChange ()
        {
            if (State == State.SetDefaults)
            {
                Description = @"News Signals for Entry based off jtEconNews2a";
                Name = "News Signals";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                USOnlyEvents = true;
                Debug = false;
                NewsRefeshInterval = 15;
                ShowLowPriority = false;
                Use24timeFormat = false;
                TodaysNewsOnly = true;
                SendAlerts = true;
                AlertInterval = 15;
                MaxNewsItems = 10;
                AlertWavFileName = "Alert1.wav";
                ShowNewsDisplay = true;
                ShowBackground = false;
               
                DisplayLocation = NewsPrintLocation.TopRight;
                DisplayXOffsetPixels = 20;
                DisplayYOffsetPixels = 60;

                ShowNewsTimeBackBrush = true;

                PreNewsBlockMinutes = 5;
                PostNewsBlockMinutes = 5;
                BlockHighImpact = true;
                BlockMediumImpact = true;
                BlockLowImpact = false;

                AddPlot (Brushes.Transparent, "NewsBlock");
                AddPlot (Brushes.Transparent, "MinutesToNextNews");
                AddPlot (Brushes.Transparent, "NextImpactScore");
                AddPlot (Brushes.Transparent, "MinutesFromRecentNews");
            }
            else if (State == State.Configure)
            {
                headerTitleBrush = HeaderColor;
                defaultTextBrush = DefaultTextColor;
                warningTextBrush = WarningTextColor;
                lineHighBrush = HighPriorityColor;
                lineMedBrush = MediumPriorityColor;
                lineLowBrush = LowPriorityColor;
                lineNormalBrush = DefaultTextColor;

                lastNewsPtr = -1;
                lastMinute = DateTime.MinValue;
                list = new List<TextLine> ();

                LoadNews ();
            }
        }

        protected override void OnBarUpdate ()
        {
            if (CurrentBar < 0)
                return;

            if (Time[0] >= lastMinute.AddMinutes (1))
            {
                if (Debug)
                    Print ("OnBarUpdate running...");

                lastMinute = Time[0];

                if (lastNewsUpdate.AddMinutes (Math.Max (1, NewsRefeshInterval)) < DateTime.Now)
                    LoadNews ();

                newsItemPtr = -1;

                if (newsEvents != null && newsEvents.Length > 0)
                {
                    for (int x = 0; x < newsEvents.Length; x++)
                    {
                        NewsEvent item = newsEvents[x];

                        if (item != null && item.DateTimeLocal >= DateTime.Now)
                        {
                            newsItemPtr = x;
                            break;
                        }
                    }

                    BuildList ();
                }
                else
                {
                    BuildList ();
                }
            }

            UpdateNewsBlockState ();
            UpdatePublicPlots ();
        }

        protected override void OnRender (ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender (chartControl, chartScale);

            if (!ShowNewsDisplay)
                return;

            if (ChartPanel == null || RenderTarget == null || list == null || list.Count == 0)
                return;

            if (defaultFont == null)
                defaultFont = new SimpleFont ("Arial", 10);

            if (titleFont == null)
                titleFont = new SimpleFont ("Arial", 10) { Bold = true };

            if (lineAlertFont == null)
                lineAlertFont = new SimpleFont ("Arial", 10) { Bold = true, Italic = true };

            TextFormat formatDefaultFont = null;
            TextFormat formatHeaderFont = null;
            TextFormat formatLineAlertFont = null;
            TextFormat formatTitleFont = null;

            try
            {
                formatDefaultFont = defaultFont.ToDirectWriteTextFormat ();

                SimpleFont headerFont = new SimpleFont (defaultFont.FamilySerialize, (int)Math.Round (defaultFont.Size))
                {
                    Bold = true
                };

                formatHeaderFont = headerFont.ToDirectWriteTextFormat ();
                formatLineAlertFont = lineAlertFont.ToDirectWriteTextFormat ();
                formatTitleFont = titleFont.ToDirectWriteTextFormat ();

                RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

                widestTimeCol = 0;
                widestImpactCol = 0;
                widestDescCol = 0;
                totalHeight = 0;
                longestLine = 0;

                float renderWidth = Math.Max (1, ChartPanel.W);
                float renderHeight = Math.Max (1, ChartPanel.H);

                foreach (TextLine line in list)
                {
                    if (line == null)
                        continue;

                    TextFormat measureFormat = line.font == lineAlertFont ? formatLineAlertFont : formatDefaultFont;

                    using (TextLayout timeLayout = new TextLayout (NinjaTrader.Core.Globals.DirectWriteFactory, SafeText (line.timeColumn), measureFormat, renderWidth, Math.Max (1, line.font.TextFormatHeight)))
                    {
                        if (timeLayout.Metrics.Width > widestTimeCol)
                            widestTimeCol = timeLayout.Metrics.Width;
                    }

                    using (TextLayout impactLayout = new TextLayout (NinjaTrader.Core.Globals.DirectWriteFactory, SafeText (line.impactColumn), measureFormat, renderWidth, Math.Max (1, line.font.TextFormatHeight)))
                    {
                        if (impactLayout.Metrics.Width > widestImpactCol)
                            widestImpactCol = impactLayout.Metrics.Width;
                    }

                    using (TextLayout descLayout = new TextLayout (NinjaTrader.Core.Globals.DirectWriteFactory, SafeText (line.descColumn), measureFormat, renderWidth, Math.Max (1, line.font.TextFormatHeight)))
                    {
                        if (descLayout.Metrics.Width > widestDescCol)
                            widestDescCol = descLayout.Metrics.Width;
                    }
                }

                widestTimeCol += TIME_PAD;
                widestImpactCol += IMPACT_PAD;
                widestDescCol += DESC_PAD;

                longestLine = widestTimeCol + widestImpactCol + widestDescCol;
                totalHeight = Math.Max (defaultFont.TextFormatHeight, titleFont.TextFormatHeight);

                float rowHeight = Math.Max (1, totalHeight + 3);
                float blockWidth = Math.Max (1, longestLine + 10);
                float blockHeight = Math.Max (1, (rowHeight * list.Count) + 10);

                Vector2 startPoint = GetDisplayStartPoint (renderWidth, renderHeight, blockWidth, blockHeight);

                float startPointX = startPoint.X;
                float startPointY = startPoint.Y;

                if (ShowBackground)
                {
                    using (SharpDX.Direct2D1.Brush bgBrush = SafeBrush (BackgroundColor).ToDxBrush (RenderTarget))
                    {
                        RectangleF recBackground = new RectangleF (
                            startPointX - 5,
                            startPointY - 5,
                            blockWidth,
                            blockHeight);

                        RenderTarget.FillRectangle (recBackground, bgBrush);
                    }
                }

                for (int i = 0; i < list.Count; i++)
                {
                    TextLine line = list[i];

                    if (line == null)
                        continue;

                    TextFormat formatToUse = i == 0
                        ? formatHeaderFont
                        : (line.font == lineAlertFont ? formatLineAlertFont : formatDefaultFont);

                    float x = startPointX;
                    float y = startPointY + (i * rowHeight);

                    System.Windows.Media.Brush mediaBrush = SafeBrush (line.brush);

                    using (SharpDX.Direct2D1.Brush dxBrush = mediaBrush.ToDxBrush (RenderTarget))
                    using (TextLayout timeLayout = new TextLayout (NinjaTrader.Core.Globals.DirectWriteFactory, SafeText (line.timeColumn), formatToUse, renderWidth, rowHeight))
                    using (TextLayout impactLayout = new TextLayout (NinjaTrader.Core.Globals.DirectWriteFactory, SafeText (line.impactColumn), formatToUse, renderWidth, rowHeight))
                    using (TextLayout descLayout = new TextLayout (NinjaTrader.Core.Globals.DirectWriteFactory, SafeText (line.descColumn), formatToUse, renderWidth, rowHeight))
                    {
                        if (ShowNewsTimeBackBrush && i > 0)
                        {
                            using (SharpDX.Direct2D1.Brush timeBackDxBrush = SafeBrush (NewsTimeBackBrush).ToDxBrush (RenderTarget))
                            {
                                RectangleF timeBackRect = new RectangleF (
                                    x - 2,
                                    y - 1,
                                    Math.Max (1, widestTimeCol - 4),
                                    Math.Max (1, rowHeight));

                                RenderTarget.FillRectangle (timeBackRect, timeBackDxBrush);
                            }
                        }

                        RenderTarget.DrawTextLayout (new Vector2 (x, y), timeLayout, dxBrush, DrawTextOptions.NoSnap);

                        x += widestTimeCol;
                        RenderTarget.DrawTextLayout (new Vector2 (x, y), impactLayout, dxBrush, DrawTextOptions.NoSnap);

                        x += widestImpactCol;
                        RenderTarget.DrawTextLayout (new Vector2 (x, y), descLayout, dxBrush, DrawTextOptions.NoSnap);
                    }
                }
            }
            catch (Exception ex)
            {
                if (Debug)
                    Print ("OnRender error in NewsSignals: " + ex);
            }
            finally
            {
                if (formatDefaultFont != null)
                    formatDefaultFont.Dispose ();

                if (formatHeaderFont != null)
                    formatHeaderFont.Dispose ();

                if (formatLineAlertFont != null)
                    formatLineAlertFont.Dispose ();

                if (formatTitleFont != null)
                    formatTitleFont.Dispose ();
            }
        }

        private Vector2 GetDisplayStartPoint (float renderWidth, float renderHeight, float blockWidth, float blockHeight)
        {
            float x;
            float y;

            switch (DisplayLocation)
            {
                case NewsPrintLocation.TopRight:
                    x = ChartPanel.X + renderWidth - blockWidth - DisplayXOffsetPixels;
                    y = ChartPanel.Y + DisplayYOffsetPixels;
                    break;

                case NewsPrintLocation.BottomLeft:
                    x = ChartPanel.X + DisplayXOffsetPixels;
                    y = ChartPanel.Y + renderHeight - blockHeight - DisplayYOffsetPixels;
                    break;

                case NewsPrintLocation.BottomRight:
                    x = ChartPanel.X + renderWidth - blockWidth - DisplayXOffsetPixels;
                    y = ChartPanel.Y + renderHeight - blockHeight - DisplayYOffsetPixels;
                    break;

                case NewsPrintLocation.Custom:
                    x = ChartPanel.X + DisplayXOffsetPixels;
                    y = ChartPanel.Y + DisplayYOffsetPixels;
                    break;

                case NewsPrintLocation.TopLeft:
                default:
                    x = ChartPanel.X + DisplayXOffsetPixels;
                    y = ChartPanel.Y + DisplayYOffsetPixels;
                    break;
            }

            if (float.IsNaN (x) || float.IsInfinity (x))
                x = ChartPanel.X + 20;

            if (float.IsNaN (y) || float.IsInfinity (y))
                y = ChartPanel.Y + 60;

            return new Vector2 (Math.Max (ChartPanel.X, x), Math.Max (ChartPanel.Y, y));
        }

        private void UpdateNewsBlockState ()
        {
            newsBlockActive = false;
            minutesToNextNews = double.NaN;
            nextImpactScore = 0.0;
            minutesFromRecentNews = double.NaN;
            nextNewsTitle = string.Empty;
            nextNewsTime = Core.Globals.MinDate;

            if (newsEvents == null || newsEvents.Length == 0)
                return;

            DateTime now = State == State.Realtime ? DateTime.Now : Time[0];

            double closestFutureMinutes = double.MaxValue;
            double closestRecentMinutes = double.MaxValue;

            for (int i = 0; i < newsEvents.Length; i++)
            {
                NewsEvent item = newsEvents[i];

                if (item == null)
                    continue;

                if (!IsImpactBlocked (item.Impact))
                    continue;

                double diffMinutes = (item.DateTimeLocal - now).TotalMinutes;
                double impactScore = GetImpactScore (item.Impact);

                if (diffMinutes >= 0 && diffMinutes < closestFutureMinutes)
                {
                    closestFutureMinutes = diffMinutes;
                    minutesToNextNews = diffMinutes;
                    nextImpactScore = impactScore;
                    nextNewsTitle = item.Title ?? string.Empty;
                    nextNewsTime = item.DateTimeLocal;
                }

                if (diffMinutes <= 0)
                {
                    double recentMinutes = Math.Abs (diffMinutes);

                    if (recentMinutes < closestRecentMinutes)
                    {
                        closestRecentMinutes = recentMinutes;
                        minutesFromRecentNews = recentMinutes;
                    }
                }

                bool inPreNewsWindow = diffMinutes >= 0 && diffMinutes <= Math.Max (0, PreNewsBlockMinutes);
                bool inPostNewsWindow = diffMinutes < 0 && Math.Abs (diffMinutes) <= Math.Max (0, PostNewsBlockMinutes);

                if (inPreNewsWindow || inPostNewsWindow)
                    newsBlockActive = true;
            }
        }

        private void UpdatePublicPlots ()
        {
            if (CurrentBar < 0)
                return;

            Values[0][0] = newsBlockActive ? 1.0 : 0.0;
            Values[1][0] = double.IsNaN (minutesToNextNews) ? -1.0 : minutesToNextNews;
            Values[2][0] = nextImpactScore;
            Values[3][0] = double.IsNaN (minutesFromRecentNews) ? -1.0 : minutesFromRecentNews;
        }

        private bool IsImpactBlocked (string impact)
        {
            string cleanImpact = string.IsNullOrEmpty (impact) ? string.Empty : impact.ToUpper ();

            if (cleanImpact == "HIGH")
                return BlockHighImpact;

            if (cleanImpact == "MEDIUM")
                return BlockMediumImpact;

            if (cleanImpact == "LOW")
                return BlockLowImpact;

            return false;
        }

        private double GetImpactScore (string impact)
        {
            string cleanImpact = string.IsNullOrEmpty (impact) ? string.Empty : impact.ToUpper ();

            if (cleanImpact == "HIGH")
                return 3.0;

            if (cleanImpact == "MEDIUM")
                return 2.0;

            if (cleanImpact == "LOW")
                return 1.0;

            return 0.0;
        }

        private string SafeText (TextColumn column)
        {
            return column == null || column.text == null ? string.Empty : column.text;
        }

        private System.Windows.Media.Brush SafeBrush (System.Windows.Media.Brush brush)
        {
            return brush ?? Brushes.White;
        }

        private void BuildList ()
        {
            if (Debug)
                Print ("Building List. lastNewsPtr: " + lastNewsPtr + " newsItemPtr: " + newsItemPtr);

            list = new List<TextLine> ();

            TextLine line = new TextLine (defaultFont, headerTitleBrush ?? defaultTextBrush ?? Brushes.White);
            line.timeColumn = new TextColumn (TIME_PAD, TIME);
            line.impactColumn = new TextColumn (IMPACT_PAD, IMPACT);
            line.descColumn = new TextColumn (DESC_PAD, DESC);
            list.Add (line);

            if (newsEvents == null || newsEvents.Length == 0 || newsItemPtr < 0)
                return;

            int lineCnt = 0;

            for (int x = newsItemPtr; x < newsEvents.Length; x++)
            {
                if (x < 0 || x >= newsEvents.Length)
                    break;

                NewsEvent item = newsEvents[x];

                if (item == null)
                    continue;

                lineCnt++;

                if (lineCnt > Math.Max (1, MaxNewsItems))
                    break;

                Priority alertPriority = Priority.Low;
                System.Windows.Media.Brush lineBrush = lineNormalBrush ?? Brushes.White;

                string impact = string.IsNullOrEmpty (item.Impact) ? string.Empty : item.Impact.ToUpper ();

                if (impact == "HIGH")
                {
                    lineBrush = lineHighBrush ?? Brushes.Red;
                    alertPriority = Priority.High;
                }
                else if (impact == "MEDIUM")
                {
                    lineBrush = lineMedBrush ?? Brushes.DarkGreen;
                    alertPriority = Priority.Medium;
                }
                else if (impact == "LOW")
                {
                    lineBrush = lineLowBrush ?? Brushes.Blue;
                }
                else
                {
                    lineBrush = lineLowBrush ?? Brushes.Blue;
                }

                string tempTime;

                if (Use24timeFormat)
                    tempTime = item.DateTimeLocal.ToString ("HH:mm", ffDateTimeCulture);
                else
                    tempTime = item.DateTimeLocal.ToString ("hh:mm tt", ffDateTimeCulture);

                if (!TodaysNewsOnly)
                    tempTime = item.DateTimeLocal.ToString ("MM/dd ", ffDateTimeCulture) + tempTime;

                SimpleFont tempFont;
                TimeSpan diff = item.DateTimeLocal - DateTime.Now;
                bool isWarningWindow = diff.TotalMinutes >= 0 && diff.TotalMinutes <= Math.Max (0, AlertInterval);

                if (SendAlerts && isWarningWindow)
                {
                    string alertKey = BuildAlertKey (item);

                    if (!alertedEventKeys.Contains (alertKey))
                    {
                        string soundPath = string.IsNullOrWhiteSpace (AlertWavFileName)
                            ? string.Empty
                            : Path.Combine (NinjaTrader.Core.Globals.InstallDir, "sounds", AlertWavFileName);

                        Alert ("NewsSignalsAlert" + item.ID.ToString (),
                            alertPriority,
                            string.Format ("News Alert: {0} {1}: {2}", item.DateTimeLocal, item.Country, item.Title),
                            soundPath,
                            10,
                            Brushes.Black,
                            Brushes.Yellow);

                        alertedEventKeys.Add (alertKey);
                    }

                    tempFont = lineAlertFont;
                    lineBrush = warningTextBrush ?? WarningTextColor ?? Brushes.Yellow;
                }
                else
                {
                    tempFont = defaultFont;
                }

                line = new TextLine (tempFont, lineBrush);
                line.timeColumn = new TextColumn (TIME_PAD, tempTime);
                line.impactColumn = new TextColumn (IMPACT_PAD, item.Impact ?? string.Empty);

                string previous = item.Previous ?? string.Empty;
                string forecast = item.Forecast ?? string.Empty;
                string title = item.Title ?? string.Empty;
                string country = item.Country ?? string.Empty;

                string templine;

                if (previous.Trim ().Length == 0 && forecast.Trim ().Length == 0)
                    templine = string.Format ("{0}{1}", USOnlyEvents ? string.Empty : country + ": ", title);
                else
                    templine = string.Format ("{0}{1} ({2}/{3})", USOnlyEvents ? string.Empty : country + ": ", title, previous, forecast);

                line.descColumn = new TextColumn (DESC_PAD, templine);
                list.Add (line);
            }
        }

        private string BuildAlertKey (NewsEvent item)
        {
            if (item == null)
                return string.Empty;

            return item.DateTimeLocal.ToString ("O", CultureInfo.InvariantCulture)
                + "|"
                + (item.Country ?? string.Empty)
                + "|"
                + (item.Title ?? string.Empty);
        }

        private void LoadNews ()
        {
            // Kick off HTTP fetch on a background thread to avoid blocking the NT8 data thread
            lastNewsUpdate = DateTime.Now;
            System.Threading.Tasks.Task.Run (() => LoadNewsBackground ());
        }

        private void LoadNewsBackground ()
        {
            lastLoadError = null;

            try
            {
                if (Debug)
                {
                    Print ("LoadNews()....");
                    string[] patts = CultureInfo.CurrentCulture.DateTimeFormat.GetAllDateTimePatterns ();
                    Print ("All DateTime Patterns for culture: " + CultureInfo.CurrentCulture.Name);

                    foreach (string patt in patts)
                        Print ("    " + patt);

                    Print ("End of DateTime Patterns");
                }

                string urltweak = ffNewsUrl + "?x=" + Convert.ToString (DateTime.Now.Ticks);

                if (Debug)
                    Print ("Loading news from URL: " + urltweak);

                HttpWebRequest newsReq = (HttpWebRequest)HttpWebRequest.Create (urltweak);
                newsReq.Timeout = 5000;
                newsReq.ReadWriteTimeout = 5000;
                newsReq.UserAgent = "NinjaTrader NewsSignals";
                newsReq.CachePolicy = new HttpRequestCachePolicy (HttpRequestCacheLevel.Reload);

                using (HttpWebResponse newsResp = (HttpWebResponse)newsReq.GetResponse ())
                {
                    if (newsResp != null && newsResp.StatusCode == HttpStatusCode.OK)
                    {
                        using (Stream receiveStream = newsResp.GetResponseStream ())
                        using (StreamReader readStream = new StreamReader (receiveStream, Encoding.UTF8))
                        {
                            string xmlString = readStream.ReadToEnd ();

                            if (Debug)
                                Print ("RAW http response: " + xmlString);

                            XmlDocument newsDoc = new XmlDocument ();
                            newsDoc.LoadXml (xmlString);

                            if (newsDoc.DocumentElement == null)
                                throw new Exception ("XML document element was null.");

                            if (Debug)
                                Print ("XML news event node count: " + newsDoc.DocumentElement.ChildNodes.Count);

                            List<NewsEvent> eventList = new List<NewsEvent> ();
                            int itemId = 0;

                            for (int i = 0; i < newsDoc.DocumentElement.ChildNodes.Count; i++)
                            {
                                XmlNode eventNode = newsDoc.DocumentElement.ChildNodes[i];

                                if (eventNode == null || eventNode.NodeType != XmlNodeType.Element)
                                    continue;

                                NewsEvent newsEvent = new NewsEvent ();

                                newsEvent.Time = GetNodeText (eventNode, "time");

                                if (string.IsNullOrEmpty (newsEvent.Time))
                                    continue;

                                newsEvent.Date = GetNodeText (eventNode, "date");

                                if (string.IsNullOrEmpty (newsEvent.Date))
                                    continue;

                                if (Debug)
                                    Print (string.Format ("About to parse Date '{0}', Time '{1}'", newsEvent.Date, newsEvent.Time));

                                DateTime parsedDateTime;

                                // The FF calendar feed provides date/time strings in US Eastern Time (ET).
                                // Parse as unspecified, then convert from ET to local time.
                                if (!DateTime.TryParse (newsEvent.Date + " " + newsEvent.Time, ffDateTimeCulture, DateTimeStyles.None, out parsedDateTime))
                                    continue;

                                try
                                {
                                    TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById ("Eastern Standard Time");
                                    newsEvent.DateTimeLocal = TimeZoneInfo.ConvertTimeToUtc (
                                        DateTime.SpecifyKind (parsedDateTime, DateTimeKind.Unspecified), easternZone).ToLocalTime ();
                                }
                                catch
                                {
                                    // Fallback: treat as UTC if Eastern timezone lookup fails
                                    newsEvent.DateTimeLocal = DateTime.SpecifyKind (parsedDateTime, DateTimeKind.Utc).ToLocalTime ();
                                }

                                if (Debug)
                                    Print ("Succesfully parsed datetime: " + newsEvent.DateTimeLocal.ToString () + " to local time.");

                                DateTime startTime = DateTime.Now;
                                DateTime endTime = startTime.AddDays (1);

                                if (newsEvent.DateTimeLocal >= startTime && (!TodaysNewsOnly || newsEvent.DateTimeLocal.Date < endTime.Date))
                                {
                                    newsEvent.ID = ++itemId;
                                    newsEvent.Country = GetNodeText (eventNode, "country");

                                    if (USOnlyEvents && newsEvent.Country != "USD")
                                        continue;

                                    newsEvent.Forecast = GetNodeText (eventNode, "forecast");
                                    newsEvent.Impact = GetNodeText (eventNode, "impact");

                                    if (!ShowLowPriority && !string.IsNullOrEmpty (newsEvent.Impact) && newsEvent.Impact.ToUpper () == "LOW")
                                        continue;

                                    newsEvent.Previous = GetNodeText (eventNode, "previous");
                                    newsEvent.Title = GetNodeText (eventNode, "title");

                                    eventList.Add (newsEvent);

                                    if (Debug)
                                        Print ("Added: " + newsEvent.ToString ());
                                }
                            }

                            newsEvents = eventList.ToArray ();

                            if (Debug)
                                Print ("Added a total of " + eventList.Count + " events to array.");
                        }
                    }
                    else
                    {
                        if (newsResp == null)
                            throw new Exception ("Web response was null.");
                        else
                            throw new Exception ("Web response status code = " + newsResp.StatusCode.ToString ());
                    }
                }
            }
            catch (Exception ex)
            {
                Print ("LoadNews error in NewsSignals: " + ex.ToString ());
                Log ("LoadNews error in NewsSignals: " + ex.ToString (), LogLevel.Information);
                lastLoadError = ex.Message;
            }
        }

        private string GetNodeText (XmlNode node, string childName)
        {
            if (node == null || string.IsNullOrEmpty (childName))
                return string.Empty;

            XmlNode child = node.SelectSingleNode (childName);
            return child == null || child.InnerText == null ? string.Empty : child.InnerText;
        }

        #region Strategy Accessors

        [Browsable (false)]
        [XmlIgnore]
        public Series<double> NewsBlock
        {
            get
            {
                return Values[0];
            }
        }

        [Browsable (false)]
        [XmlIgnore]
        public Series<double> MinutesToNextNews
        {
            get
            {
                return Values[1];
            }
        }

        [Browsable (false)]
        [XmlIgnore]
        public Series<double> NextImpactScore
        {
            get
            {
                return Values[2];
            }
        }

        [Browsable (false)]
        [XmlIgnore]
        public Series<double> MinutesFromRecentNews
        {
            get
            {
                return Values[3];
            }
        }

        [Browsable (false)]
        [XmlIgnore]
        public bool IsNewsBlockActive
        {
            get
            {
                return newsBlockActive;
            }
        }

        [Browsable (false)]
        [XmlIgnore]
        public string NextNewsTitle
        {
            get
            {
                return nextNewsTitle;
            }
        }

        [Browsable (false)]
        [XmlIgnore]
        public DateTime NextNewsTime
        {
            get
            {
                return nextNewsTime;
            }
        }

        #endregion

        #region Properties

        // ─────────────────────────────────────────────────────────────
        // Display
        // ─────────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display (Name = "Show News Display", Description = "Show or hide the rendered news table on the chart. Strategy plots still update when hidden.", Order = 0, GroupName = "Display")]
        public bool ShowNewsDisplay
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Display Location", Description = "Chart location for the news table.", Order = 1, GroupName = "Display")]
        public NewsPrintLocation DisplayLocation
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 5000)]
        [Display (Name = "Display X Offset Pixels", Description = "Horizontal offset from the selected chart location.", Order = 2, GroupName = "Display")]
        public int DisplayXOffsetPixels
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 5000)]
        [Display (Name = "Display Y Offset Pixels", Description = "Vertical offset from the selected chart location.", Order = 3, GroupName = "Display")]
        public int DisplayYOffsetPixels
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Use 24-Hour Time Format", Description = "Display news event times using 24-hour format instead of AM/PM.", Order = 4, GroupName = "Display")]
        public bool Use24timeFormat
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show Background", Description = "Show or hide the background behind the news table.", Order = 5, GroupName = "Display")]
        public bool ShowBackground
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show News Time BackBrush", Description = "Show or hide the background color behind the news release time column.", Order = 6, GroupName = "Display")]
        public bool ShowNewsTimeBackBrush
        {
            get; set;
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "News Time BackBrush", Description = "Background color behind the news release time column.", Order = 7, GroupName = "Display")]
        public System.Windows.Media.Brush NewsTimeBackBrush
        {
            get
            {
                return newsTimeBackBrush;
            }
            set
            {
                newsTimeBackBrush = value;
            }
        }

        [Browsable (false)]
        public string NewsTimeBackBrushSerialize
        {
            get
            {
                return Serialize.BrushToString (newsTimeBackBrush);
            }
            set
            {
                newsTimeBackBrush = Serialize.StringToBrush (value);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // News Filter
        // ─────────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display (Name = "US Events Only", Description = "Show only USD news events.", Order = 0, GroupName = "News Filter")]
        public bool USOnlyEvents
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Today's News Only", Description = "Show only news events scheduled for today.", Order = 1, GroupName = "News Filter")]
        public bool TodaysNewsOnly
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Show Low Priority", Description = "Show low-priority news events.", Order = 2, GroupName = "News Filter")]
        public bool ShowLowPriority
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Max News Items", Description = "Maximum number of pending news events to display.", Order = 3, GroupName = "News Filter")]
        public int MaxNewsItems
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "News Refresh Interval", Description = "News refresh interval in minutes.", Order = 4, GroupName = "News Filter")]
        public int NewsRefeshInterval
        {
            get; set;
        }

        // ─────────────────────────────────────────────────────────────
        // Strategy Blocking
        // ─────────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Range (0, 240)]
        [Display (Name = "Pre-News Block Minutes", Description = "Block trading this many minutes before a matching news release.", Order = 0, GroupName = "Strategy Blocking")]
        public int PreNewsBlockMinutes
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range (0, 240)]
        [Display (Name = "Post-News Block Minutes", Description = "Block trading this many minutes after a matching news release.", Order = 1, GroupName = "Strategy Blocking")]
        public int PostNewsBlockMinutes
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Block High Impact", Description = "Block strategy trading for high-impact news events.", Order = 2, GroupName = "Strategy Blocking")]
        public bool BlockHighImpact
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Block Medium Impact", Description = "Block strategy trading for medium-impact news events.", Order = 3, GroupName = "Strategy Blocking")]
        public bool BlockMediumImpact
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Block Low Impact", Description = "Block strategy trading for low-impact news events.", Order = 4, GroupName = "Strategy Blocking")]
        public bool BlockLowImpact
        {
            get; set;
        }

        // ─────────────────────────────────────────────────────────────
        // Alerts
        // ─────────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display (Name = "Send Alerts", Description = "Send alerts to the NinjaTrader Alerts window.", Order = 0, GroupName = "Alerts")]
        public bool SendAlerts
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Alert Interval", Description = "Number of minutes before a news event to trigger an alert.", Order = 1, GroupName = "Alerts")]
        public int AlertInterval
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display (Name = "Alert WAV File", Description = "Alert WAV file name. Leave blank to disable alert audio.", Order = 2, GroupName = "Alerts")]
        public string AlertWavFileName
        {
            get; set;
        }

        // ─────────────────────────────────────────────────────────────
        // Colors
        // ─────────────────────────────────────────────────────────────
        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Default Text Color", Description = "Default text color for the news display.", Order = 0, GroupName = "Colors")]
        public System.Windows.Media.Brush DefaultTextColor
        {
            get
            {
                return defaultTextColor;
            }
            set
            {
                defaultTextColor = value;
            }
        }

        [Browsable (false)]
        public string DefaultTextColorSerialize
        {
            get
            {
                return Serialize.BrushToString (defaultTextColor);
            }
            set
            {
                defaultTextColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Warning Text Color", Description = "Text color for news events inside the alert/warning window.", Order = 1, GroupName = "Colors")]
        public System.Windows.Media.Brush WarningTextColor
        {
            get
            {
                return warningTextColor;
            }
            set
            {
                warningTextColor = value;
            }
        }

        [Browsable (false)]
        public string WarningTextColorSerialize
        {
            get
            {
                return Serialize.BrushToString (warningTextColor);
            }
            set
            {
                warningTextColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Background Color", Description = "Background color behind the news table.", Order = 2, GroupName = "Colors")]
        public System.Windows.Media.Brush BackgroundColor
        {
            get
            {
                return backgroundColor;
            }
            set
            {
                backgroundColor = value;
            }
        }

        [Browsable (false)]
        public string BackgroundColorSerialize
        {
            get
            {
                return Serialize.BrushToString (backgroundColor);
            }
            set
            {
                backgroundColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Header Text Color", Description = "Header row text color.", Order = 3, GroupName = "Colors")]
        public System.Windows.Media.Brush HeaderColor
        {
            get
            {
                return headerColor;
            }
            set
            {
                headerColor = value;
            }
        }

        [Browsable (false)]
        public string HeaderColorSerialize
        {
            get
            {
                return Serialize.BrushToString (headerColor);
            }
            set
            {
                headerColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "High Impact Text Color", Description = "Text color for high-impact news events.", Order = 4, GroupName = "Colors")]
        public System.Windows.Media.Brush HighPriorityColor
        {
            get
            {
                return lineHighColor;
            }
            set
            {
                lineHighColor = value;
            }
        }

        [Browsable (false)]
        public string HighPriorityColorSerialize
        {
            get
            {
                return Serialize.BrushToString (lineHighColor);
            }
            set
            {
                lineHighColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Medium Impact Text Color", Description = "Text color for medium-impact news events.", Order = 5, GroupName = "Colors")]
        public System.Windows.Media.Brush MediumPriorityColor
        {
            get
            {
                return lineMedColor;
            }
            set
            {
                lineMedColor = value;
            }
        }

        [Browsable (false)]
        public string MediumPriorityColorSerialize
        {
            get
            {
                return Serialize.BrushToString (lineMedColor);
            }
            set
            {
                lineMedColor = Serialize.StringToBrush (value);
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Low Impact Text Color", Description = "Text color for low-impact news events.", Order = 6, GroupName = "Colors")]
        public System.Windows.Media.Brush LowPriorityColor
        {
            get
            {
                return lineLowColor;
            }
            set
            {
                lineLowColor = value;
            }
        }

        [Browsable (false)]
        public string LowPriorityColorSerialize
        {
            get
            {
                return Serialize.BrushToString (lineLowColor);
            }
            set
            {
                lineLowColor = Serialize.StringToBrush (value);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Fonts
        // ─────────────────────────────────────────────────────────────
        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Default Font", Description = "Default font for news events.", Order = 0, GroupName = "Fonts")]
        public SimpleFont DefaultFont
        {
            get
            {
                return defaultFont;
            }
            set
            {
                defaultFont = value;
            }
        }

        [Browsable (false)]
        public string DefaultFontSerialize
        {
            get
            {
                return defaultFont.FamilySerialize;
            }
            set
            {
                defaultFont = new SimpleFont (value, DefaultFontSizeSerialize);
            }
        }

        [Browsable (false)]
        public double DefaultFontSizeSerialize
        {
            get
            {
                return DefaultFont.Size;
            }
            set
            {
                DefaultFont.Size = value;
            }
        }

        [XmlIgnore ()]
        [NinjaScriptProperty]
        [Display (Name = "Warning Font", Description = "Font for news events inside the alert/warning window.", Order = 1, GroupName = "Fonts")]
        public SimpleFont WarningFont
        {
            get
            {
                return lineAlertFont;
            }
            set
            {
                lineAlertFont = value;
            }
        }

        [Browsable (false)]
        public string WarningFontSerialize
        {
            get
            {
                return lineAlertFont.FamilySerialize;
            }
            set
            {
                lineAlertFont = new SimpleFont (value, WarningFontSizeSerialize);
            }
        }

        [Browsable (false)]
        public double WarningFontSizeSerialize
        {
            get
            {
                return WarningFont.Size;
            }
            set
            {
                WarningFont.Size = value;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Debug
        // ─────────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display (Name = "Debug", Description = "Print debug information to the NinjaTrader Output window.", Order = 0, GroupName = "Debug")]
        public bool Debug
        {
            get; set;
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Playr101.NewsSignals[] cacheNewsSignals;
		public Playr101.NewsSignals NewsSignals(bool showNewsDisplay, NewsPrintLocation displayLocation, int displayXOffsetPixels, int displayYOffsetPixels, bool use24timeFormat, bool showBackground, bool showNewsTimeBackBrush, System.Windows.Media.Brush newsTimeBackBrush, bool uSOnlyEvents, bool todaysNewsOnly, bool showLowPriority, int maxNewsItems, int newsRefeshInterval, int preNewsBlockMinutes, int postNewsBlockMinutes, bool blockHighImpact, bool blockMediumImpact, bool blockLowImpact, bool sendAlerts, int alertInterval, string alertWavFileName, System.Windows.Media.Brush defaultTextColor, System.Windows.Media.Brush warningTextColor, System.Windows.Media.Brush backgroundColor, System.Windows.Media.Brush headerColor, System.Windows.Media.Brush highPriorityColor, System.Windows.Media.Brush mediumPriorityColor, System.Windows.Media.Brush lowPriorityColor, SimpleFont defaultFont, SimpleFont warningFont, bool debug)
		{
			return NewsSignals(Input, showNewsDisplay, displayLocation, displayXOffsetPixels, displayYOffsetPixels, use24timeFormat, showBackground, showNewsTimeBackBrush, newsTimeBackBrush, uSOnlyEvents, todaysNewsOnly, showLowPriority, maxNewsItems, newsRefeshInterval, preNewsBlockMinutes, postNewsBlockMinutes, blockHighImpact, blockMediumImpact, blockLowImpact, sendAlerts, alertInterval, alertWavFileName, defaultTextColor, warningTextColor, backgroundColor, headerColor, highPriorityColor, mediumPriorityColor, lowPriorityColor, defaultFont, warningFont, debug);
		}

		public Playr101.NewsSignals NewsSignals(ISeries<double> input, bool showNewsDisplay, NewsPrintLocation displayLocation, int displayXOffsetPixels, int displayYOffsetPixels, bool use24timeFormat, bool showBackground, bool showNewsTimeBackBrush, System.Windows.Media.Brush newsTimeBackBrush, bool uSOnlyEvents, bool todaysNewsOnly, bool showLowPriority, int maxNewsItems, int newsRefeshInterval, int preNewsBlockMinutes, int postNewsBlockMinutes, bool blockHighImpact, bool blockMediumImpact, bool blockLowImpact, bool sendAlerts, int alertInterval, string alertWavFileName, System.Windows.Media.Brush defaultTextColor, System.Windows.Media.Brush warningTextColor, System.Windows.Media.Brush backgroundColor, System.Windows.Media.Brush headerColor, System.Windows.Media.Brush highPriorityColor, System.Windows.Media.Brush mediumPriorityColor, System.Windows.Media.Brush lowPriorityColor, SimpleFont defaultFont, SimpleFont warningFont, bool debug)
		{
			if (cacheNewsSignals != null)
				for (int idx = 0; idx < cacheNewsSignals.Length; idx++)
					if (cacheNewsSignals[idx] != null && cacheNewsSignals[idx].ShowNewsDisplay == showNewsDisplay && cacheNewsSignals[idx].DisplayLocation == displayLocation && cacheNewsSignals[idx].DisplayXOffsetPixels == displayXOffsetPixels && cacheNewsSignals[idx].DisplayYOffsetPixels == displayYOffsetPixels && cacheNewsSignals[idx].Use24timeFormat == use24timeFormat && cacheNewsSignals[idx].ShowBackground == showBackground && cacheNewsSignals[idx].ShowNewsTimeBackBrush == showNewsTimeBackBrush && cacheNewsSignals[idx].NewsTimeBackBrush == newsTimeBackBrush && cacheNewsSignals[idx].USOnlyEvents == uSOnlyEvents && cacheNewsSignals[idx].TodaysNewsOnly == todaysNewsOnly && cacheNewsSignals[idx].ShowLowPriority == showLowPriority && cacheNewsSignals[idx].MaxNewsItems == maxNewsItems && cacheNewsSignals[idx].NewsRefeshInterval == newsRefeshInterval && cacheNewsSignals[idx].PreNewsBlockMinutes == preNewsBlockMinutes && cacheNewsSignals[idx].PostNewsBlockMinutes == postNewsBlockMinutes && cacheNewsSignals[idx].BlockHighImpact == blockHighImpact && cacheNewsSignals[idx].BlockMediumImpact == blockMediumImpact && cacheNewsSignals[idx].BlockLowImpact == blockLowImpact && cacheNewsSignals[idx].SendAlerts == sendAlerts && cacheNewsSignals[idx].AlertInterval == alertInterval && cacheNewsSignals[idx].AlertWavFileName == alertWavFileName && cacheNewsSignals[idx].DefaultTextColor == defaultTextColor && cacheNewsSignals[idx].WarningTextColor == warningTextColor && cacheNewsSignals[idx].BackgroundColor == backgroundColor && cacheNewsSignals[idx].HeaderColor == headerColor && cacheNewsSignals[idx].HighPriorityColor == highPriorityColor && cacheNewsSignals[idx].MediumPriorityColor == mediumPriorityColor && cacheNewsSignals[idx].LowPriorityColor == lowPriorityColor && cacheNewsSignals[idx].DefaultFont == defaultFont && cacheNewsSignals[idx].WarningFont == warningFont && cacheNewsSignals[idx].Debug == debug && cacheNewsSignals[idx].EqualsInput(input))
						return cacheNewsSignals[idx];
			return CacheIndicator<Playr101.NewsSignals>(new Playr101.NewsSignals(){ ShowNewsDisplay = showNewsDisplay, DisplayLocation = displayLocation, DisplayXOffsetPixels = displayXOffsetPixels, DisplayYOffsetPixels = displayYOffsetPixels, Use24timeFormat = use24timeFormat, ShowBackground = showBackground, ShowNewsTimeBackBrush = showNewsTimeBackBrush, NewsTimeBackBrush = newsTimeBackBrush, USOnlyEvents = uSOnlyEvents, TodaysNewsOnly = todaysNewsOnly, ShowLowPriority = showLowPriority, MaxNewsItems = maxNewsItems, NewsRefeshInterval = newsRefeshInterval, PreNewsBlockMinutes = preNewsBlockMinutes, PostNewsBlockMinutes = postNewsBlockMinutes, BlockHighImpact = blockHighImpact, BlockMediumImpact = blockMediumImpact, BlockLowImpact = blockLowImpact, SendAlerts = sendAlerts, AlertInterval = alertInterval, AlertWavFileName = alertWavFileName, DefaultTextColor = defaultTextColor, WarningTextColor = warningTextColor, BackgroundColor = backgroundColor, HeaderColor = headerColor, HighPriorityColor = highPriorityColor, MediumPriorityColor = mediumPriorityColor, LowPriorityColor = lowPriorityColor, DefaultFont = defaultFont, WarningFont = warningFont, Debug = debug }, input, ref cacheNewsSignals);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Playr101.NewsSignals NewsSignals(bool showNewsDisplay, NewsPrintLocation displayLocation, int displayXOffsetPixels, int displayYOffsetPixels, bool use24timeFormat, bool showBackground, bool showNewsTimeBackBrush, System.Windows.Media.Brush newsTimeBackBrush, bool uSOnlyEvents, bool todaysNewsOnly, bool showLowPriority, int maxNewsItems, int newsRefeshInterval, int preNewsBlockMinutes, int postNewsBlockMinutes, bool blockHighImpact, bool blockMediumImpact, bool blockLowImpact, bool sendAlerts, int alertInterval, string alertWavFileName, System.Windows.Media.Brush defaultTextColor, System.Windows.Media.Brush warningTextColor, System.Windows.Media.Brush backgroundColor, System.Windows.Media.Brush headerColor, System.Windows.Media.Brush highPriorityColor, System.Windows.Media.Brush mediumPriorityColor, System.Windows.Media.Brush lowPriorityColor, SimpleFont defaultFont, SimpleFont warningFont, bool debug)
		{
			return indicator.NewsSignals(Input, showNewsDisplay, displayLocation, displayXOffsetPixels, displayYOffsetPixels, use24timeFormat, showBackground, showNewsTimeBackBrush, newsTimeBackBrush, uSOnlyEvents, todaysNewsOnly, showLowPriority, maxNewsItems, newsRefeshInterval, preNewsBlockMinutes, postNewsBlockMinutes, blockHighImpact, blockMediumImpact, blockLowImpact, sendAlerts, alertInterval, alertWavFileName, defaultTextColor, warningTextColor, backgroundColor, headerColor, highPriorityColor, mediumPriorityColor, lowPriorityColor, defaultFont, warningFont, debug);
		}

		public Indicators.Playr101.NewsSignals NewsSignals(ISeries<double> input , bool showNewsDisplay, NewsPrintLocation displayLocation, int displayXOffsetPixels, int displayYOffsetPixels, bool use24timeFormat, bool showBackground, bool showNewsTimeBackBrush, System.Windows.Media.Brush newsTimeBackBrush, bool uSOnlyEvents, bool todaysNewsOnly, bool showLowPriority, int maxNewsItems, int newsRefeshInterval, int preNewsBlockMinutes, int postNewsBlockMinutes, bool blockHighImpact, bool blockMediumImpact, bool blockLowImpact, bool sendAlerts, int alertInterval, string alertWavFileName, System.Windows.Media.Brush defaultTextColor, System.Windows.Media.Brush warningTextColor, System.Windows.Media.Brush backgroundColor, System.Windows.Media.Brush headerColor, System.Windows.Media.Brush highPriorityColor, System.Windows.Media.Brush mediumPriorityColor, System.Windows.Media.Brush lowPriorityColor, SimpleFont defaultFont, SimpleFont warningFont, bool debug)
		{
			return indicator.NewsSignals(input, showNewsDisplay, displayLocation, displayXOffsetPixels, displayYOffsetPixels, use24timeFormat, showBackground, showNewsTimeBackBrush, newsTimeBackBrush, uSOnlyEvents, todaysNewsOnly, showLowPriority, maxNewsItems, newsRefeshInterval, preNewsBlockMinutes, postNewsBlockMinutes, blockHighImpact, blockMediumImpact, blockLowImpact, sendAlerts, alertInterval, alertWavFileName, defaultTextColor, warningTextColor, backgroundColor, headerColor, highPriorityColor, mediumPriorityColor, lowPriorityColor, defaultFont, warningFont, debug);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Playr101.NewsSignals NewsSignals(bool showNewsDisplay, NewsPrintLocation displayLocation, int displayXOffsetPixels, int displayYOffsetPixels, bool use24timeFormat, bool showBackground, bool showNewsTimeBackBrush, System.Windows.Media.Brush newsTimeBackBrush, bool uSOnlyEvents, bool todaysNewsOnly, bool showLowPriority, int maxNewsItems, int newsRefeshInterval, int preNewsBlockMinutes, int postNewsBlockMinutes, bool blockHighImpact, bool blockMediumImpact, bool blockLowImpact, bool sendAlerts, int alertInterval, string alertWavFileName, System.Windows.Media.Brush defaultTextColor, System.Windows.Media.Brush warningTextColor, System.Windows.Media.Brush backgroundColor, System.Windows.Media.Brush headerColor, System.Windows.Media.Brush highPriorityColor, System.Windows.Media.Brush mediumPriorityColor, System.Windows.Media.Brush lowPriorityColor, SimpleFont defaultFont, SimpleFont warningFont, bool debug)
		{
			return indicator.NewsSignals(Input, showNewsDisplay, displayLocation, displayXOffsetPixels, displayYOffsetPixels, use24timeFormat, showBackground, showNewsTimeBackBrush, newsTimeBackBrush, uSOnlyEvents, todaysNewsOnly, showLowPriority, maxNewsItems, newsRefeshInterval, preNewsBlockMinutes, postNewsBlockMinutes, blockHighImpact, blockMediumImpact, blockLowImpact, sendAlerts, alertInterval, alertWavFileName, defaultTextColor, warningTextColor, backgroundColor, headerColor, highPriorityColor, mediumPriorityColor, lowPriorityColor, defaultFont, warningFont, debug);
		}

		public Indicators.Playr101.NewsSignals NewsSignals(ISeries<double> input , bool showNewsDisplay, NewsPrintLocation displayLocation, int displayXOffsetPixels, int displayYOffsetPixels, bool use24timeFormat, bool showBackground, bool showNewsTimeBackBrush, System.Windows.Media.Brush newsTimeBackBrush, bool uSOnlyEvents, bool todaysNewsOnly, bool showLowPriority, int maxNewsItems, int newsRefeshInterval, int preNewsBlockMinutes, int postNewsBlockMinutes, bool blockHighImpact, bool blockMediumImpact, bool blockLowImpact, bool sendAlerts, int alertInterval, string alertWavFileName, System.Windows.Media.Brush defaultTextColor, System.Windows.Media.Brush warningTextColor, System.Windows.Media.Brush backgroundColor, System.Windows.Media.Brush headerColor, System.Windows.Media.Brush highPriorityColor, System.Windows.Media.Brush mediumPriorityColor, System.Windows.Media.Brush lowPriorityColor, SimpleFont defaultFont, SimpleFont warningFont, bool debug)
		{
			return indicator.NewsSignals(input, showNewsDisplay, displayLocation, displayXOffsetPixels, displayYOffsetPixels, use24timeFormat, showBackground, showNewsTimeBackBrush, newsTimeBackBrush, uSOnlyEvents, todaysNewsOnly, showLowPriority, maxNewsItems, newsRefeshInterval, preNewsBlockMinutes, postNewsBlockMinutes, blockHighImpact, blockMediumImpact, blockLowImpact, sendAlerts, alertInterval, alertWavFileName, defaultTextColor, warningTextColor, backgroundColor, headerColor, highPriorityColor, mediumPriorityColor, lowPriorityColor, defaultFont, warningFont, debug);
		}
	}
}

#endregion
