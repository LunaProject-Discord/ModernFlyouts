using System.Linq;
using System.Windows.Controls;

namespace ModernFlyouts.Controls
{
    public partial class VirtualDesktopControl : UserControl
    {
        public VirtualDesktopControl()
        {
            InitializeComponent();
        }

        public void Update(string name, int index, int count)
        {
            NameTextBlock.Text = name;
            IndicatorItems.ItemsSource = Enumerable.Range(0, count).Select(i => i == index).ToArray();
        }
    }
}
