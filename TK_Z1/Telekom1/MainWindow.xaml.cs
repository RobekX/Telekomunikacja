using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
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

namespace Telekom1
{
    /// <summary>
    /// Logika interakcji dla klasy MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Coder calc = new Coder();
        public MainWindow()
        {
            InitializeComponent();
        }


        private void EncodeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            EncodeButton.IsEnabled = Input.Text != "";
        }

        private void DecodeOutput_TextChanged(object sender, TextChangedEventArgs e)
        {
            DecodeButton.IsEnabled = Output.Text != "";
        }

        private void EncodeButton_Click(object sender, RoutedEventArgs e)
        {
            //ARG: EncodeInput.Text
            string ToBeEncoded = Input.Text;
            Output.Text=calc.encodeString(Input.Text);
            //saveFiles();
        }

        private void DecodeButton_Click(object sender, RoutedEventArgs e)
        {
            String output = "";
            try
            {
                
                 Button_Click(sender, e);
                String input = Output.Text;

                output = calc.decodeString(input);
                Input.Text = output;
               
                stat.Content="Decoded encoded.txt";
            }
            catch (Exception)
            {
                stat.Content = "Too many errors!";
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                String output = Output.Text;
                output = output.Replace(" ", "");
                output = output.Replace("\n", "");
                String[] stringBytes = new String[output.Length / 8];

                // podziel całość na 8 znakowe Stringi
                for (int i = 0, j = 0; i < output.Length; i += 8, j++)
                {
                    stringBytes[j] = output.Substring(i, 8);
                    
                }

                byte[] bytes = new byte[stringBytes.Length];

                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = calc.bin2ASCII(stringBytes[i]);
                }
                File.WriteAllBytes("encodedText.txt", bytes);
            }
            catch (IOException)
            {
                stat.Content = "Cannot save encoded.txt";
            }
        }

        private void saveText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                File.WriteAllText("inputText.txt",Input.Text);
            }
            catch (IOException)
            {
                
            }
        }
    }
}
