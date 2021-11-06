﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using IWshRuntimeLibrary;
using Serilog;
using XIVLauncher.Accounts;
using XIVLauncher.Http;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    /// Interaction logic for AccountSwitcher.xaml
    /// </summary>
    public partial class AccountSwitcher : Window
    {
        private readonly AccountManager _accountManager;

        public EventHandler<XivAccount> OnAccountSwitchedEventHandler;

        public AccountSwitcher(AccountManager accountManager)
        {
            InitializeComponent();

            DataContext = new AccountSwitcherViewModel();

            _accountManager = accountManager;

            RefreshEntries();
        }

        private void RefreshEntries()
        {
            var accountEntries = new List<AccountSwitcherEntry>();

            foreach (var accountManagerAccount in _accountManager.Accounts)
            {
                if (string.IsNullOrEmpty(accountManagerAccount.ThumbnailUrl))
                    accountManagerAccount.ThumbnailUrl = accountManagerAccount.FindCharacterThumb();

                accountEntries.Add(new AccountSwitcherEntry
                {
                    Account = accountManagerAccount
                });

                accountEntries.Last().UpdateProfileImage();
            }

            _accountManager.Save();

            AccountListView.ItemsSource = accountEntries;
        }

        private bool _closing = false;

        private void AccountListView_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            var selectedEntry = AccountListView.SelectedItem as AccountSwitcherEntry;

            OnAccountSwitchedEventHandler?.Invoke(this, selectedEntry.Account);

            _closing = true;
            Close();
        }

        private void AccountListViewContext_Opened(object sender, RoutedEventArgs e)
        {
            var selectedEntry = AccountListView.SelectedItem as AccountSwitcherEntry;
            AccountEntrySavePasswordCheck.IsChecked = !selectedEntry.Account.SavePassword;
        }

        private void AccountSwitcher_OnDeactivated(object sender, EventArgs e)
        {
            if (!_closing)
                Close();
        }

        private Bitmap BitmapImage2Bitmap(BitmapImage bitmapImage)
        {
            using(MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(outStream);

                return new Bitmap(bitmap);
            }
        }

        // https://stackoverflow.com/questions/11434673/bitmap-save-to-save-an-icon-actually-saves-a-png
        void SaveAsIcon(Bitmap SourceBitmap, string FilePath)
        {
            FileStream FS = new FileStream(FilePath, FileMode.Create);
            // ICO header
            FS.WriteByte(0); FS.WriteByte(0);
            FS.WriteByte(1); FS.WriteByte(0);
            FS.WriteByte(1); FS.WriteByte(0);

            // Image size
            FS.WriteByte((byte)SourceBitmap.Width);
            FS.WriteByte((byte)SourceBitmap.Height);
            // Palette
            FS.WriteByte(0);
            // Reserved
            FS.WriteByte(0);
            // Number of color planes
            FS.WriteByte(0); FS.WriteByte(0);
            // Bits per pixel
            FS.WriteByte(32); FS.WriteByte(0);

            // Data size, will be written after the data
            FS.WriteByte(0);
            FS.WriteByte(0);
            FS.WriteByte(0);
            FS.WriteByte(0);

            // Offset to image data, fixed at 22
            FS.WriteByte(22);
            FS.WriteByte(0);
            FS.WriteByte(0);
            FS.WriteByte(0);

            // Writing actual data
            SourceBitmap.Save(FS, ImageFormat.Png);

            // Getting data length (file length minus header)
            long Len = FS.Length - 22;

            // Write it in the correct place
            FS.Seek(14, SeekOrigin.Begin);
            FS.WriteByte((byte)Len);
            FS.WriteByte((byte)(Len >> 8));

            FS.Close();
        }

        private void CreateDesktopShortcut_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(AccountListView.SelectedItem is AccountSwitcherEntry selectedEntry))
                return;

            var thumbnailPath = System.Reflection.Assembly.GetEntryAssembly().Location;

            if (!string.IsNullOrEmpty(selectedEntry.Account.ThumbnailUrl))
            {
                var thumbnailDirectory = Path.Combine(Paths.RoamingPath, "profileIcons");
                Directory.CreateDirectory(thumbnailDirectory);

                thumbnailPath = Path.Combine(thumbnailDirectory, $"{selectedEntry.Account.Id}.ico");

                SaveAsIcon(BitmapImage2Bitmap((BitmapImage) selectedEntry.ProfileImage), thumbnailPath);
            }

            var shDesktop = (object)"Desktop";

            var shell = new WshShell();
            var shortcutAddress = (string)shell.SpecialFolders.Item(ref shDesktop) + $@"\XIVLauncher - {selectedEntry.Account.UserName} {(selectedEntry.Account.UseSteamServiceAccount ? "(Steam)" : "")}.lnk";
            var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutAddress);
            shortcut.Description = $"Open XIVLauncher with the {selectedEntry.Account.UserName} Square Enix account.";
            shortcut.TargetPath = Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent.FullName, "XIVLauncher.exe");
            shortcut.Arguments = $"--account={selectedEntry.Account.Id}";
            shortcut.WorkingDirectory = Environment.CurrentDirectory;
            shortcut.IconLocation = thumbnailPath;
            shortcut.Save();
        }

        private void RemoveAccount_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(AccountListView.SelectedItem is AccountSwitcherEntry selectedEntry))
                return;

            _accountManager.RemoveAccount(selectedEntry.Account);

            RefreshEntries();
        }

        private void SetProfilePicture_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(AccountListView.SelectedItem is AccountSwitcherEntry selectedEntry))
                return;

            var inputDialog = new ProfilePictureInputWindow(selectedEntry.Account);
            inputDialog.ShowDialog();

            var account = _accountManager.Accounts.First(a => a.Id == selectedEntry.Account.Id);
            account.ChosenCharacterName = inputDialog.ResultName;
            account.ChosenCharacterWorld = inputDialog.ResultWorld;
            _accountManager.Save();

            RefreshEntries();
        }

        private void DontSavePassword_OnChecked(object sender, RoutedEventArgs e)
        {
            if (!(AccountListView.SelectedItem is AccountSwitcherEntry selectedEntry))
                return;

            var account = _accountManager.Accounts.First(a => a.Id == selectedEntry.Account.Id);
            account.SavePassword = false;
            account.Password = string.Empty;
            _accountManager.Save();
        }

        private void DontSavePassword_OnUnchecked(object sender, RoutedEventArgs e)
        {
            if (!(AccountListView.SelectedItem is AccountSwitcherEntry selectedEntry))
                return;

            var account = _accountManager.Accounts.First(a => a.Id == selectedEntry.Account.Id);
            account.SavePassword = true;
            _accountManager.Save();
        }

        private void ModifyOtpUri_Click(object sender, RoutedEventArgs e)
        {
            if (!(AccountListView.SelectedItem is AccountSwitcherEntry selectedEntry))
                return;

            var otpDialog = new OtpUriSetupWindow(selectedEntry.Account.OtpUri);
            otpDialog.ShowDialog();

            var account = _accountManager.Accounts.First(a => a.Id == selectedEntry.Account.Id);
            account.OtpUri = otpDialog.Result;
            _accountManager.Save();
        }
    }
}
