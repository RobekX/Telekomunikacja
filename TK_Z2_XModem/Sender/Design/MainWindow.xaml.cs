using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Design
{
    /// <summary>
    /// Logika interakcji dla klasy MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string path = "";
        public MainWindow()
        {
            InitializeComponent();
            coms.DropDownClosed += (s, e) =>
            {
                if (coms.Text.StartsWith("COM") && path.Length != 0)
                {
                    sendButton.IsEnabled = true;
                    loger.Content = "COM selected.";
                }
                    
            };
            SerialPort.GetPortNames().ToList().ForEach(x => coms.Items.Add(x));
        }


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog file = new OpenFileDialog();
            file.Title = "Choose file...";
            file.Multiselect = false;
            if (file.ShowDialog() == true)
            {
                path = file.FileName;
                textbox.Text = path;
                loger.Content = "File selected.";
                if (coms.Text.StartsWith("COM"))
                    sendButton.IsEnabled = true;
            }
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            SerialPort comPort = new SerialPort(coms.Text, 115200, Parity.None, 8, StopBits.One);
            Xmodem modem = new Xmodem(comPort);
            loger.Content = "Sending file...";
            byte[] dataToSend = File.ReadAllBytes(path);
            await Task.Run(() => modem.Send(dataToSend));
            loger.Content = "File sent.";
            comPort.Close();
        }
    }
}
