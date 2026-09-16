using libeLog.WinApi.Windows;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Keyboard = libeLog.WinApi.Windows.Keyboard;

namespace eLog.Views.Controls
{
    /// <summary>
    /// Логика взаимодействия для LatinKeyboard.xaml
    /// </summary>
    public partial class LatinKeyboard : UserControl
    {
        private bool _CapsEnabled;

        /// <summary> Caps Lock: выставляется родителем <see cref="KeyboardControl"/>, запоминается. </summary>
        public bool CapsEnabled
        {
            get => _CapsEnabled;
            set
            {
                _CapsEnabled = value;
                UpdateLetterCase();
            }
        }

        public LatinKeyboard()
        {
            InitializeComponent();
        }

        /// <summary> Отправка буквы с учётом Caps (через Shift, системный CapsLock не трогаем). </summary>
        private void SendLetter(Keys key)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            if (CapsEnabled)
            {
                Keyboard.KeyDown(Keys.LShiftKey);
                Keyboard.KeyPress(key);
                Keyboard.KeyUp(Keys.LShiftKey);
            }
            else
            {
                Keyboard.KeyPress(key);
            }
        }

        private void QButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.Q);
        }

        private void WButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.W);
        }

        private void EButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.E);
        }

        private void RButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.R);
        }

        private void TButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.T);
        }

        private void YButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.Y);
        }

        private void UButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.U);
        }

        private void IButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.I);
        }

        private void OButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.O);
        }

        private void PButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.P);
        }

        private void LsbButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyPress(Keys.Oem2);
        }

        private void AButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.A);
        }

        private void SButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.S);
        }

        private void DButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.D);
        }

        private void FButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.F);
        }

        private void GButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.G);
        }

        private void HButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.H);
        }

        private void JButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.J);
        }

        private void KButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.K);
        }

        private void LButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.L);
        }

        private void ColonButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyDown(Keys.LShiftKey);
            Keyboard.KeyPress(Keys.D9);
            Keyboard.KeyUp(Keys.LShiftKey);
        }

        private void ApButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyDown(Keys.LShiftKey);
            Keyboard.KeyPress(Keys.D0);
            Keyboard.KeyUp(Keys.LShiftKey);
        }

        private void ZButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.Z);
        }

        private void XButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.X);
        }

        private void CButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.C);
        }

        private void VButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.V);
        }

        private void BButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.B);
        }

        private void NButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.N);
        }

        private void MButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            SendLetter(Keys.M);
        }

        private void LtButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyPress(Keys.Oemcomma);
        }

        private void GtButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyPress(Keys.OemPeriod);
        }

        private void QmButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyDown(Keys.LShiftKey);
            Keyboard.KeyPress(Keys.Oem2);
            Keyboard.KeyUp(Keys.LShiftKey);
        }

        private void DashButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyPress(Keys.OemMinus);
        }

        /// <summary> Отображаемый регистр букв следует за Caps; символы не трогаем. </summary>
        private void UpdateLetterCase()
        {
            QButton.Content = _CapsEnabled ? "Q" : "q";
            WButton.Content = _CapsEnabled ? "W" : "w";
            EButton.Content = _CapsEnabled ? "E" : "e";
            RButton.Content = _CapsEnabled ? "R" : "r";
            TButton.Content = _CapsEnabled ? "T" : "t";
            YButton.Content = _CapsEnabled ? "Y" : "y";
            UButton.Content = _CapsEnabled ? "U" : "u";
            IButton.Content = _CapsEnabled ? "I" : "i";
            OButton.Content = _CapsEnabled ? "O" : "o";
            PButton.Content = _CapsEnabled ? "P" : "p";
            AButton.Content = _CapsEnabled ? "A" : "a";
            SButton.Content = _CapsEnabled ? "S" : "s";
            DButton.Content = _CapsEnabled ? "D" : "d";
            FButton.Content = _CapsEnabled ? "F" : "f";
            GButton.Content = _CapsEnabled ? "G" : "g";
            HButton.Content = _CapsEnabled ? "H" : "h";
            JButton.Content = _CapsEnabled ? "J" : "j";
            KButton.Content = _CapsEnabled ? "K" : "k";
            LButton.Content = _CapsEnabled ? "L" : "l";
            ZButton.Content = _CapsEnabled ? "Z" : "z";
            XButton.Content = _CapsEnabled ? "X" : "x";
            CButton.Content = _CapsEnabled ? "C" : "c";
            VButton.Content = _CapsEnabled ? "V" : "v";
            BButton.Content = _CapsEnabled ? "B" : "b";
            NButton.Content = _CapsEnabled ? "N" : "n";
            MButton.Content = _CapsEnabled ? "M" : "m";
        }

        private void ColonSymbolButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyPress(Keys.Oem1);
        }
    }
}