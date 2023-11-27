using System.Windows;

namespace andyScreenSaver
{
    /// <summary>
    /// Interaction logic for Window2.xaml
    /// </summary>
    public partial class Window2 : Window
    {
        string verificationCode = "";
        public Window2()
        {
            InitializeComponent();
        }

        public string getCode()
        {
            return verificationCode;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            verificationCode = tbVerificationCode.Text;
            Close();
        }
    }
}
