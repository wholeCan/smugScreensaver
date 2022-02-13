/** 
 * Original work by Andrew Holkan
 * Date: 2/1/2013
 * Contact info: aholkan@gmail.com
 * **/


using SMEngine;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
namespace andyScreenSaver
{


    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    ///
    public partial class SettingsWindow : Window
    {
        //private RaindropSettings settings;
        //public override void BeginInit()
        //{
        //    groupBox1.Visibility = System.Windows.Visibility.Hidden;
        //    base.BeginInit();
        //}
        public async void initEngine()
        {
            _engine = new CSMEngine();
            await this.Dispatcher.BeginInvoke(new Action(async delegate ()
            {


                dataGrid1.ItemsSource = _engine._galleryTable.DefaultView;
                comboBox3.Items.Add("Tiny");
                comboBox3.Items.Add("Small");
                comboBox3.Items.Add("Medium");
                comboBox3.Items.Add("Large");
                comboBox3.Items.Add("Extra Large");
                comboBox3.Items.Add("Original");
                comboBox3.SelectedIndex = 2;

                loadLoginInfo();
                bool connected = await connect();
                if (!connected)
                {
                    //start up new process to authenticate.
                    authenticateApp();
                    //if (!connect())
                    //  MessageBox.Show("Error connecting, try again");
                }
                else
                {
                    button1.Content = "Validated";
                    button1.IsEnabled = false;
                }

            }));


        }
        public SettingsWindow()
        {
            InitializeComponent();
            groupBox1.Visibility = System.Windows.Visibility.Hidden;
            ThreadStart ts = new ThreadStart(initEngine);
            Thread t = new Thread(ts);
            t.IsBackground = true;
            t.Start();
        }
        private void saveSettings()
        {
            //  settings.Save(RaindropSettings.SettingsFile);
            CSettings set = new CSettings();
            set.quality = comboBox3.SelectedIndex;
            set.speed_s = Convert.ToInt32(slider1.Value);
            set.load_all = (bool)cbAlwaysLoadAllGalleries.IsChecked;
            set.showInfo = (bool)cbShowInfo.IsChecked;
            set.gridHeight = int.Parse(gridHeight.Text);
            set.gridWidth = int.Parse(gridWidth.Text);
            set.borderThickness = int.Parse(BorderThickness.Text);
            _engine.setSettings(set);
            _engine.saveConfiguration();

        }
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            saveSettings();
            this.Close();
        }

        /// <summary>
        /// Set all sliders to their default values
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnDefaults_Click(object sender, RoutedEventArgs e)
        {
            // settings.SetDefaults();
            _engine._galleryTable.Clear();
            _engine.setSettings(new CSettings());
            //tLogin.Text = "";
            //tPassword.Password = "";

            //SetSliders();
        }



        private CSMEngine _engine;
        private void loadLoginInfo()
        {
            loginInfo _login = _engine.getLogin();


            comboBox3.SelectedIndex = _engine.settings.quality;

            gridHeight.Text = _engine.settings.gridHeight.ToString();
            gridWidth.Text = _engine.settings.gridWidth.ToString();

            BorderThickness.Text = _engine.settings.borderThickness.ToString();
            slider1.Value = _engine.settings.speed_s;
            cbAlwaysLoadAllGalleries.IsChecked = _engine.settings.load_all;

            if ((bool)cbAlwaysLoadAllGalleries.IsChecked)
            {
                //if ((bool)checkBox1.IsChecked)
                groupBox1.IsEnabled = false;
                groupBox1.Visibility = System.Windows.Visibility.Hidden;
                //else
                //    groupBox1.Visibility = System.Windows.Visibility.Visible;
            }

            else// private void checkBox1_Unchecked(object sender, RoutedEventArgs e)
            {
                groupBox1.Visibility = System.Windows.Visibility.Visible;
                groupBox1.IsEnabled = true;
            }

            cbShowInfo.IsChecked = _engine.settings.showInfo;
        }


        private void authenticateApp()
        {
            // Prepare the process to run
            ProcessStartInfo start = new ProcessStartInfo();
            // Enter in the command line arguments, everything you would enter after the executable name itself
            start.Arguments = "";
            // Enter the executable to run, including the complete path
            start.FileName = ConfigurationSettings.AppSettings["ConfigApp"];  //@"C:\Users\aholk\Documents\PROJECTS\2018Upgrade\screenSaver\screenSaver\setupApp\bin\Debug\setupApp.exe";

            // Do you want to show a console window?
            start.WindowStyle = ProcessWindowStyle.Normal;
            start.CreateNoWindow = false;
            int exitCode;


            // Run the external process & wait for it to finish
            using (Process proc = Process.Start(start))
            {
                proc.WaitForExit();

                // Retrieve the app's exit code
                exitCode = proc.ExitCode;

            }


        }
        private async Task<bool> connect()
        {
            bool success = false;

            Cursor t = this.Cursor;
            try
            {
                Cursor = Cursors.Wait;
                loginInfo _login = new loginInfo();
                _engine.saveConfiguration(_login);

                success = _engine.login();
                // success = false;  // test code!

                if (success)
                {
                    comboBox1.Items.Add("Waiting for data...");
                    String[] Cats = await _engine.getCategoriesAsync();
                    comboBox1.Items.Clear();
                    comboBox2.Items.Clear();
                    foreach (String s in Cats)
                    {
                        if (_engine.checkCategoryForAlbums(s))
                            comboBox1.Items.Add(s);
                    }
                    if (comboBox1.HasItems)
                    {
                        comboBox1.SelectedIndex = 0;
                        comboBox2.IsEnabled = true;
                        button2.IsEnabled = true;
                        button3.IsEnabled = true;

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                Cursor = t;
            }
            return success;
        }
        bool tmpStarted = false;
        private async void button1_Click(object sender, RoutedEventArgs e)
        {
            tmpStarted = true;
            var connected = await connect();
            if (!connected)
            {

                //start up new process to authenticate.
                authenticateApp();
                connected = await connect();
                if (!connected)
                {
                    MessageBox.Show("Incorrect Authentication");
                }
                else
                {
                    button1.Content = "Validated";
                    button1.IsEnabled = false;
                }
            }
            else
            {
                button1.Content = "Validated";
                button1.IsEnabled = false;
            }

        }

        private void comboBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            comboBox2.Items.Clear();
            String[] albums = _engine.getAlbums(comboBox1.SelectedValue.ToString());
            foreach (String s in albums)
            {
                comboBox2.Items.Add(s);
            }
            if (comboBox2.HasItems)
                comboBox2.SelectedIndex = 0;
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            if (tmpStarted)
                MessageBox.Show("You need to close and restart the slideshow for settings to take effect.");
            if (comboBox1.SelectedValue != null && comboBox2.SelectedValue != null)
            {
                _engine.addGallery(comboBox1.SelectedValue.ToString(), comboBox2.SelectedValue.ToString());
            }
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {

            _engine.addAllAlbums();

        }

        private void button4_Click(object sender, RoutedEventArgs e)
        {
            dataGrid1.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    _engine._galleryTable.Clear();
                }));
        }

        private void checkBox1_Checked(object sender, RoutedEventArgs e)
        {
            groupBox1.IsEnabled = false;
            groupBox1.Visibility = System.Windows.Visibility.Hidden;
        }

        private void checkBox1_Unchecked(object sender, RoutedEventArgs e)
        {
            groupBox1.Visibility = System.Windows.Visibility.Visible;
            groupBox1.IsEnabled = true;
        }

        private void tLogin_TextChanged(object sender, TextChangedEventArgs e)
        {
            button1.Content = "Connect";
            button1.IsEnabled = true;
        }

        private void tPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            button1.Content = "Connect";
            button1.IsEnabled = true;
        }

        private void button6_Click(object sender, RoutedEventArgs e)
        {
            //email
            MessageBox.Show("Send an email to: aholkan@gmail.com");

        }

        private void button5_Click(object sender, RoutedEventArgs e)
        {
            //donate
            MessageBox.Show("No fee is necessary, but if you like my software I would appreciate anything you contribute.  Thanks for clicking!\r\n\r\nYour web browser will now load my payment page.");
            String paypalLink = "https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=R434RMQYFAKBG";
            Process.Start(paypalLink);
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

            Window1 win = new Window1();

            win.setDimensions(333, 200);
            win.init();
            win.disableActions();

            win.WindowState = WindowState.Normal;
            win.WindowStyle = WindowStyle.SingleBorderWindow;
            win.ResizeMode = ResizeMode.CanResizeWithGrip;
            win.Show();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            saveSettings();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            addAllAlbumsforCategory();
        }

        private void addAllAlbumsforCategory()
        {
            if (comboBox1.SelectedValue != null)
            {
                _engine.addAllAlbums(comboBox1.SelectedValue.ToString());

            }


        }
    }
    public class RoundingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        {
            if (value != null)
            {
                double d = (double)value;
                return ((int)d).ToString() + " s.";
            }
            return 0;
        }
        public object ConvertBack(object value, Type ttype, object p, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

    }
}
