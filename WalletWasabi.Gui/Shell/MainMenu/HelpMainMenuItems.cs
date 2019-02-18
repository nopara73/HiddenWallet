﻿using AvalonStudio.MainMenu;
using AvalonStudio.Menus;
using System;
using System.Collections.Generic;
using System.Composition;
using System.Text;

namespace WalletWasabi.Gui.Shell.MainMenu
{
	internal class HelpMainMenuItems
	{
		private IMenuItemFactory MenuItemFactory { get; }

		[ImportingConstructor]
		public HelpMainMenuItems(IMenuItemFactory menuItemFactory)
		{
			MenuItemFactory = menuItemFactory;
		}

		#region MainMenu

		[ExportMainMenuItem("Help")]
		[DefaultOrder(2)]
		public IMenuItem Tools => MenuItemFactory.CreateHeaderMenuItem("Help", null);

		#endregion MainMenu

		#region Group

		[ExportMainMenuDefaultGroup("Help", "About")]
		[DefaultOrder(0)]
		public object AboutGroup => null;

		[ExportMainMenuDefaultGroup("Help", "Support")]
		[DefaultOrder(1)]
		public object SupportGroup => null;

		[ExportMainMenuDefaultGroup("Help", "Legal")]
		[DefaultOrder(2)]
		public object LegalGroup => null;

		#endregion Group

		#region MenuItem

		[ExportMainMenuItem("Help", "About")]
		[DefaultOrder(1)]
		[DefaultGroup("About")]
		public IMenuItem About => MenuItemFactory.CreateCommandMenuItem("Help.About");

#if DEBUG

		[ExportMainMenuItem("Help", "Dev Tools")]
		[DefaultOrder(2)]
		[DefaultGroup("About")]
		public IMenuItem DevTools => MenuItemFactory.CreateCommandMenuItem("Help.DevTools");

#endif

		[ExportMainMenuItem("Help", "CustomerSupport")]
		[DefaultOrder(3)]
		[DefaultGroup("Support")]
		public IMenuItem CustomerSupport => MenuItemFactory.CreateCommandMenuItem("Help.CustomerSupport");

		[ExportMainMenuItem("Help", "ReportBug")]
		[DefaultOrder(4)]
		[DefaultGroup("Support")]
		public IMenuItem ReportBug => MenuItemFactory.CreateCommandMenuItem("Help.ReportBug");

		[ExportMainMenuItem("Help", "PrivacyPolicy")]
		[DefaultOrder(5)]
		[DefaultGroup("Legal")]
		public IMenuItem PrivacyPolicy => MenuItemFactory.CreateCommandMenuItem("Help.PrivacyPolicy");

		[ExportMainMenuItem("Help", "TermsAndConditions")]
		[DefaultOrder(6)]
		[DefaultGroup("Legal")]
		public IMenuItem TermsAndConditions => MenuItemFactory.CreateCommandMenuItem("Help.TermsAndConditions");

		[ExportMainMenuItem("Help", "LegalIssues")]
		[DefaultOrder(7)]
		[DefaultGroup("Legal")]
		public IMenuItem LegalIssues => MenuItemFactory.CreateCommandMenuItem("Help.LegalIssues");

		#endregion MenuItem
	}
}
