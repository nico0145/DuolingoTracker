using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pepperoni.Duolingo;
using System.Xml.Linq;
using System.IO;
using OxyPlot.Wpf;
using OxyPlot;

namespace DuolingoTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Points OPoints { set; get; }
        public Users OUsers { set; get; }
        public IList<DataPoint> SrPoints { get; private set; }
        public List<Series> NonDisplayedSeries { set; get; }
        public List<PrefixedLineSeries> PrefixedLineSeries { set; get; }
        public const string FileName = "DuoLingoPoints.xml";
        public MainWindow()
        {
            InitializeComponent();
        }
        public void LoadFile(string sFile)
        {
            if (!File.Exists(sFile))
            {
                using StreamWriter sw = File.AppendText(sFile);
                sw.Write(@"<Root><Users<User><Alias>AliasHere</Alias><UserName>UsernameHere</UserName><Password>PlainPwdHere</Password></User></Users><Points></Points></Root>");
            }
            XElement xDocs = XElement.Load(sFile);
            OPoints = new Points(xDocs.Descendants("Points").First());
            OUsers = new Users(xDocs.Descendants("Users").First());
        }
        public void SetGraph()
        {
            pltMain.Series.Clear();
            NonDisplayedSeries.Clear();
            innerStack.Children.Clear();
            DateTime dMin = OPoints.Min(x => x.Date);
            dtAxis = new DateTimeAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, Minimum = dMin.Date.ToOADate(), Maximum = DateTime.Today.ToOADate(), StringFormat = "M/d" };
            dtAxis.FirstDateTime = dMin.Date;
            dtAxis.LastDateTime = DateTime.Today;
            PrefixedLineSeries = new List<PrefixedLineSeries>();
            OUsers.ForEach(x => PrefixedLineSeries.Add(OPoints.ToDailyPoints(x.Alias)));
            OUsers.ForEach(x => PrefixedLineSeries.Add(OPoints.ToHourlyDatePoints(x.Alias)));
            OUsers.ForEach(x => PrefixedLineSeries.Add(OPoints.ToTotalAvg(x.Alias)));
            OUsers.ForEach(x => PrefixedLineSeries.Add(OPoints.ToWeeklyAvg(x.Alias)));
            foreach (var PrefLine in PrefixedLineSeries.OrderBy(x => x.Title))
            {
                pltMain.Series.Add(PrefLine.LineSeries);
                CheckBox checkBox = new CheckBox()
                {
                    Name = PrefLine.Key,
                    Content = PrefLine.Title,
                    IsChecked = true
                };
                checkBox.Click += new RoutedEventHandler(this.Combo_Clicked);
                innerStack.Children.Add(checkBox);
            }
            RefreshGraph();
        }
        public void SaveFile(string sFile)
        {
            XElement xDoc = XElement.Load(sFile);
            var xPoints = xDoc.Descendants("Points").First();
            foreach (var oPoint in OPoints.Where(x => x.New))
            {
                xPoints.Add(oPoint.ToXMLElement());
            }
            xDoc.Save(sFile);
        }
        private void Combo_Clicked(object sender, RoutedEventArgs e)
        {
            var cb = (CheckBox)sender;

            if (cb.IsChecked.GetValueOrDefault())
            {
                var s = NonDisplayedSeries.FirstOrDefault(x => x.Name == "s" + cb.Name);
                NonDisplayedSeries.Remove(s);
                pltMain.Series.Add(s);
            }
            else
            {
                var s = pltMain.Series.FirstOrDefault(s => s.Name == "s" + cb.Name);
                pltMain.Series.Remove(s);
                NonDisplayedSeries.Add(s);
            }
            RefreshGraph();
        }
        private void RefreshGraph()
        {
            Dispatcher.InvokeAsync(() =>
            {
                pltMain.InvalidatePlot(true);
            });
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NonDisplayedSeries = new List<OxyPlot.Wpf.Series>();
            Refresh();
        }
        private void Refresh()
        {
            LoadFile(FileName);
            OUsers.ForEach(x => GetUserPoints(x));
            SetGraph();
            SaveFile(FileName);
        }
        private void GetUserPoints(User user)
        {
            DuolingoClient duolingoClient = new DuolingoClient(user.UserName, user.Password);
            OPoints.AppendNewPoints(duolingoClient.User.XpGains, user.Alias);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }
    }
    public class Users : List<User>
    {
        public Users(XElement oRaw)
        {
            foreach (XElement xRow in oRaw.Elements())
            {
                Add(new User(xRow));
            }
        }
    }
    public class User
    {
        public string Alias { set; get; }
        public string UserName { set; get; }
        public string Password { set; get; }
        public XElement ToXMLElement()
        {
            XElement oElement = new XElement("User");
            oElement.SetElementValue("Alias", Alias);
            oElement.SetElementValue("UserName", UserName);
            oElement.SetElementValue("Password", Password);
            return oElement;
        }
        public User(XElement oRaw)
        {
            Alias = oRaw.Element("Alias").Value;
            UserName = oRaw.Element("UserName").Value;
            Password = oRaw.Element("Password").Value;
        }
    }
    public class Points : List<Point>
    {
        public Points(XElement oRaw)
        {
            foreach (XElement xRow in oRaw.Elements())
            {
                Add(new Point(xRow));
            }
        }
        public void AppendNewPoints(XpGain[] oIn, string sUser)
        {
            foreach (var oGain in oIn.Where(x => !Exists(y => y.TimeStamp == x.Time && sUser == y.User)))
            {
                Add(new Point(oGain, sUser));
            }
        }
        public PrefixedLineSeries ToDailyPoints(string sAlias)
        {

            return new PrefixedLineSeries(ToDataPoints(GetDailyPoints(sAlias)), sAlias, "Daily");
        }
        private List<Point> GetDailyPoints(string sAlias)
        {
            var lstRetu = new List<Point>();
            DateTime dAux = this.Min(x => x.Date).Date;
            while (dAux < DateTime.Today.AddDays(1))
            {
                lstRetu.Add(new Point(dAux.AddDays(1).AddMilliseconds(-1), this.Where(x => x.Date.Date == dAux && x.User == sAlias)?.Sum(x => x.XP) ?? 0));
                dAux = dAux.AddDays(1);
            }
            return lstRetu;
        }
        public PrefixedLineSeries ToHourlyDatePoints(string sAlias)
        {
            var lstRetu = new List<Point>();
            var sortedList = this.Where(x => x.User == sAlias).OrderBy(x => x.TimeStamp);
            DateTime dAux = sortedList.Min(x => x.Date).Date;
            long xpAcum = 0;
            dAux = sortedList.Min(x => x.Date).Date;
            while (dAux < DateTime.Today.AddDays(1))
            {
                lstRetu.Add(new Point(dAux.AddMilliseconds(-1), xpAcum));
                xpAcum = 0;
                lstRetu.Add(new Point(dAux, xpAcum));
                foreach (var oPoint in sortedList.Where(x => x.Date.Date == dAux))
                {
                    lstRetu.Add(new Point(oPoint.Date.AddMilliseconds(-1), xpAcum));
                    xpAcum += oPoint.XP;
                    lstRetu.Add(new Point(oPoint.Date, xpAcum));
                }
                dAux = dAux.AddDays(1);
            }
            return new PrefixedLineSeries(ToDataPoints(lstRetu), sAlias, "Hourly");
        }
        public PrefixedLineSeries ToTotalAvg(string sAlias)
        {
            var lstDaily = GetDailyPoints(sAlias);
            List<Point> lstRetu = new List<Point>();
            long Accum = 0;
            int iCount = 0;
            foreach (var point in lstDaily)
            {
                iCount++;
                Accum += point.XP;
                lstRetu.Add(new Point(point.Date, (Accum / iCount)));
            }
            return new PrefixedLineSeries(ToDataPoints(lstRetu), sAlias, "TotalAverage");
        }
        public PrefixedLineSeries ToWeeklyAvg(string sAlias)
        {
            var lstDaily = GetDailyPoints(sAlias);
            List<Point> lstRetu = new List<Point>();
            List<long> Scores = new List<long>();
            foreach (var point in lstDaily)
            {
                if (Scores.Count == 7)
                {
                    Scores.RemoveAt(0);
                }
                Scores.Add(point.XP);
                lstRetu.Add(new Point(point.Date, (Scores.Sum() / Scores.Count)));
            }
            return new PrefixedLineSeries(ToDataPoints(lstRetu), sAlias, "WeeklyAverage");
        }
        private IList<DataPoint> ToDataPoints(List<Point> points)
        {
            IList<DataPoint> fs = new List<DataPoint>();
            foreach (var point in points)
            {
                fs.Add(point.ToDataPoint());
            }
            return fs;
        }

    }
    public class PrefixedLineSeries
    {
        public PrefixedLineSeries(IList<DataPoint> dataPoints, string sAlias, string sCategory)
        {
            DataPoints = dataPoints;
            Alias = sAlias;
            Category = sCategory;
        }
        public IList<DataPoint> DataPoints { set; get; }
        public OxyPlot.Wpf.LineSeries LineSeries
        {
            get
            {
                return new OxyPlot.Wpf.LineSeries { ItemsSource = DataPoints, Name = "s" + Key, Title = Title };
            }
        }
        public string Alias { set; get; }
        public string Category { set; get; }
        public string Key
        {
            get
            {
                return Category + Alias;
            }
        }
        public string Title
        {
            get
            {
                return $"{Alias} {Category}";
            }
        }
    }
    public class Point
    {
        public override string ToString()
        {
            return Date.ToShortDateString() + " - " + XP.ToString();
        }
        public Point(DateTime date, long xP)
        {
            Date = date;
            XP = xP;
        }
        public DataPoint ToDataPoint()
        {
            return new DataPoint(Date.ToOADate(), XP);
        }
        public Point(XElement oRaw)
        {
            if (long.TryParse(oRaw.Element("TimeStamp").Value, out long lTimestamp))
            {
                TimeStamp = lTimestamp;
            }
            if (long.TryParse(oRaw.Element("XP").Value, out long iXP))
            {
                XP = iXP;
            }
            User = oRaw.Element("User").Value;
            New = false;
        }
        public Point(XpGain oRaw, string sUser)
        {
            XP = oRaw.Xp.GetValueOrDefault();
            TimeStamp = oRaw.Time.GetValueOrDefault();
            New = true;
            User = sUser;
        }
        public bool New { set; get; }
        public long TimeStamp { set; get; }
        public long XP { set; get; }
        private DateTime? mDate;
        public string User { set; get; }
        public DateTime Date
        {
            get
            {
                if (!mDate.HasValue)
                {
                    DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
                    dtDateTime = dtDateTime.AddSeconds(TimeStamp).ToLocalTime();
                    return dtDateTime;
                }
                else
                    return mDate.Value;
            }
            set
            {
                mDate = value;
            }
        }
        public XElement ToXMLElement()
        {
            XElement oElement = new XElement("Point");
            oElement.SetElementValue("XP", XP);
            oElement.SetElementValue("TimeStamp", TimeStamp);
            oElement.SetElementValue("User", User);
            return oElement;
        }
    }
}
