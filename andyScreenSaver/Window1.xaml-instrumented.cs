using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WaveSim
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class Window1 : Window
    {
        private Vector3D zoomDelta;

        private WaveGrid _grid;
        private DispatcherTimer _timer;

        const string MyEventSource = "WaveSim";
        private int _eventCounter;

        public Window1()
        {
            InitializeComponent();

            // Configure event log for performance monitoring
            if (!EventLog.SourceExists(MyEventSource))
                EventLog.CreateEventSource(MyEventSource, "Application");
            EventLog.WriteEntry(MyEventSource, "* WaveSim startup", EventLogEntryType.Information);

            _grid = new WaveGrid(10);        // 10x10 grid
            _grid.SetCenterPeak(3.0);
            meshMain.Positions = _grid.Points;
            meshMain.TriangleIndices = _grid.TriangleIndices;

            // On each WheelMouse change, we zoom in/out a particular % of the original distance
            const double ZoomPctEachWheelChange = 0.02;
            zoomDelta = Vector3D.Multiply(ZoomPctEachWheelChange, camMain.LookDirection);
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                // Zoom in
                camMain.Position = Point3D.Add(camMain.Position, zoomDelta);
            else
                // Zoom out
                camMain.Position = Point3D.Subtract(camMain.Position, zoomDelta);
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            EventLog.WriteEntry(MyEventSource, "WaveSim start simulation", EventLogEntryType.Information);
            _eventCounter = 0;

            _grid = new WaveGrid(10);        // 10x10 grid
            _grid.SetCenterPeak(3.0);
            meshMain.Positions = _grid.Points;

            _timer = new DispatcherTimer();
            _timer.Tick += new EventHandler(timer_Tick);
            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Start();
        }

        void timer_Tick(object sender, EventArgs e)
        {
            _timer.Stop();      // Stop timer, in case it ticks again before we finish work
            StringBuilder sbPerfInfo = new StringBuilder();

            QueryPerfCounter perfTimer = new QueryPerfCounter();
            double perfDur;

            // Unhook Positions collection from our mesh, for performance
            // (see http://blogs.msdn.com/timothyc/archive/2006/08/31/734308.aspx)
            meshMain.Positions = null;

            perfTimer.Start();

            // Do the next iteration on the water grid, propagating waves
            _grid.ProcessWater();    

            perfTimer.Stop();
            perfDur = perfTimer.Duration(1);
            sbPerfInfo.AppendFormat("ProcessWater took {0}.  ", perfDur.ToString());

            perfTimer.Start();

            // Then update our mesh to use new Z values
            meshMain.Positions = _grid.Points;
            perfTimer.Stop();
            perfDur = perfTimer.Duration(1);
            sbPerfInfo.AppendFormat("UpdateMeshZValues took {0}", perfDur.ToString());

            // Write perf data to event log
            EventLog.WriteEntry(MyEventSource, sbPerfInfo.ToString(), EventLogEntryType.Information);

            _timer.Start();     // Restart timer that controls frequency of wave propagation
        }
    }
}
