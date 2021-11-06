﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CheapLoc;

namespace XIVLauncher.Windows.ViewModel
{
    class AccountSwitcherViewModel
    {
        public AccountSwitcherViewModel()
        {
            SetupLoc();
        }

        private void SetupLoc()
        {
            AccountSwitcherSetProfilePicLoc = Loc.Localize("AccountSwitcherSetProfilePic", "Set profile picture");
            AccountSwitcherCreateShortcutLoc = Loc.Localize("AccountSwitcherCreateShortcut", "Create desktop shortcut");
            RemoveLoc = Loc.Localize("Remove", "Remove");
            AccountSwitcherDontSavePasswordLoc = Loc.Localize("AccountSwitcherDontSavePassword", "Don't save password");
            ModifyOtpUriLoc = Loc.Localize("ModifyOtpUriLoc", "Modify OTP URI");
        }

        public string AccountSwitcherSetProfilePicLoc { get; private set; }
        public string AccountSwitcherCreateShortcutLoc { get; private set; }
        public string RemoveLoc { get; private set; }
        public string AccountSwitcherDontSavePasswordLoc { get; private set; }
        public string ModifyOtpUriLoc { get; private set; }
    }
}
