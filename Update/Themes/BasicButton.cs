using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;


namespace Updater
{
    public class BasicButton : Button
    {
        static BasicButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BasicButton), new FrameworkPropertyMetadata(typeof(BasicButton)));
        }


        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            TextBlock tb = (TextBlock)Template.FindName("tb", this);
            tb.FontSize = Height / 2.2;
        }


        public Brush FocBrush
        {
            get { return (Brush)GetValue(FocBrushProperty); }
            set { SetValue(FocBrushProperty, value); }
        }
        public static readonly DependencyProperty FocBrushProperty =
         DependencyProperty.Register("FocBrush", typeof(Brush), typeof(BasicButton), new PropertyMetadata());
    }
}
