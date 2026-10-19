using libeLog.WinApi.Windows;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Keyboard = libeLog.WinApi.Windows.Keyboard;

namespace eLog.Views.Controls
{
    /// <summary>
    /// Логика взаимодействия для CyrillicKeyboard.xaml
    /// </summary>
    public partial class CyrillicKeyboard : UserControl
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

        public CyrillicKeyboard()
        {
            InitializeComponent();
        }

        /// <summary> Отправка буквы с учётом Caps (через Shift, системный CapsLock не трогаем). </summary>
        private void SendLetter(Keys key)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
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
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.Q);
        }

        private void WButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.W);
        }

        private void EButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.E);
        }

        private void RButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.R);
        }

        private void TButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.T);
        }

        private void YButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.Y);
        }

        private void UButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.U);
        }

        private void IButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.I);
        }

        private void OButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.O);
        }

        private void PButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.P);
        }

        private void LsbButton_Click(object sender, RoutedEventArgs e)
        {
            SendLetter(Keys.Oem4);
        }

        private void AButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.A);
        }

        private void SButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.S);
        }

        private void DButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.D);
        }

        private void FButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.F);
        }

        private void GButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.G);
        }

        private void HButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.H);
        }

        private void JButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.J);
        }

        private void KButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.K);
        }

        private void LButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.L);
        }

        private void ColonButton_Click(object sender, RoutedEventArgs e)
        {
            SendLetter(Keys.Oem1);
        }

        private void ApButton_Click(object sender, RoutedEventArgs e)
        {
            SendLetter(Keys.Oem7);
        }

        private void ZButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.Z);
        }

        private void XButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.X);
        }

        private void CButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.C);
        }

        private void VButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.V);
        }

        private void BButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.B);
        }

        private void NButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.N);
        }

        private void MButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            SendLetter(Keys.M);
        }

        private void LtButton_Click(object sender, RoutedEventArgs e)
        {
            SendLetter(Keys.Oemcomma);
        }

        private void GtButton_Click(object sender, RoutedEventArgs e)
        {
            SendLetter(Keys.OemPeriod);
        }

        private void QmButton_Click(object sender, RoutedEventArgs e)
        {
            SendLetter(Keys.Oem6);
        }

        private void Add1Button_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.Ru)).Activate();
            Keyboard.KeyDown(Keys.LShiftKey);
            Keyboard.KeyPress(Keys.Oem2);
            Keyboard.KeyUp(Keys.LShiftKey);
        }

        private void YoButton_Click(object sender, RoutedEventArgs e)
        {
            SendLetter(Keys.Oem3);
        }

        /// <summary> Отображаемый регистр букв следует за Caps; символы не трогаем. </summary>
        private void UpdateLetterCase()
        {
            QButton.Content = _CapsEnabled ? "Й" : "й";
            WButton.Content = _CapsEnabled ? "Ц" : "ц";
            EButton.Content = _CapsEnabled ? "У" : "у";
            RButton.Content = _CapsEnabled ? "К" : "к";
            TButton.Content = _CapsEnabled ? "Е" : "е";
            YButton.Content = _CapsEnabled ? "Н" : "н";
            UButton.Content = _CapsEnabled ? "Г" : "г";
            IButton.Content = _CapsEnabled ? "Ш" : "ш";
            OButton.Content = _CapsEnabled ? "Щ" : "щ";
            PButton.Content = _CapsEnabled ? "З" : "з";
            AButton.Content = _CapsEnabled ? "Ф" : "ф";
            SButton.Content = _CapsEnabled ? "Ы" : "ы";
            DButton.Content = _CapsEnabled ? "В" : "в";
            FButton.Content = _CapsEnabled ? "А" : "а";
            GButton.Content = _CapsEnabled ? "П" : "п";
            HButton.Content = _CapsEnabled ? "Р" : "р";
            JButton.Content = _CapsEnabled ? "О" : "о";
            KButton.Content = _CapsEnabled ? "Л" : "л";
            LButton.Content = _CapsEnabled ? "Д" : "д";
            ZButton.Content = _CapsEnabled ? "Я" : "я";
            XButton.Content = _CapsEnabled ? "Ч" : "ч";
            CButton.Content = _CapsEnabled ? "С" : "с";
            VButton.Content = _CapsEnabled ? "М" : "м";
            BButton.Content = _CapsEnabled ? "И" : "и";
            NButton.Content = _CapsEnabled ? "Т" : "т";
            MButton.Content = _CapsEnabled ? "Ь" : "ь";
            LsbButton.Content = _CapsEnabled ? "Х" : "х";
            ColonButton.Content = _CapsEnabled ? "Ж" : "ж";
            ApButton.Content = _CapsEnabled ? "Э" : "э";
            LtButton.Content = _CapsEnabled ? "Б" : "б";
            GtButton.Content = _CapsEnabled ? "Ю" : "ю";
            QmButton.Content = _CapsEnabled ? "Ъ" : "ъ";
            YoButton.Content = _CapsEnabled ? "Ё" : "ё";
        }

        private void SlashButton_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayout.Load(CultureInfo.GetCultureInfo(KeyboardLayout.En)).Activate();
            Keyboard.KeyPress(Keys.Oem2);
        }
    }
}