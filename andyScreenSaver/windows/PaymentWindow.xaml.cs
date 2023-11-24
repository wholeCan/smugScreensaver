using System;
using System.Diagnostics;
using System.Windows;

namespace andyScreenSaver.windows
{
    /// <summary>
    /// Interaction logic for PaymentWindow.xaml
    /// </summary>
    public partial class PaymentWindow : Window
    {
        public PaymentWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            String paypalLink = "https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=R434RMQYFAKBG";
            Process.Start(paypalLink);
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            var link = "https://venmo.com/code?user_id=2201983276548096911&created=1647049634.504469&printed=1";
            Process.Start(link);
        }
    }
}
