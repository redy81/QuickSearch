using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QuickSearch
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            var ver = Assembly.GetExecutingAssembly().GetName().Version;

            if ( ver is not null )
            {
                VersionText.Text = "Version " + ver.ToString();
            }
            else
            {
                VersionText.Text = "Version --.--";
            }
        }
    }
}
