﻿using DXVisualTestFixer.Services;
using System.Windows;
using Prism.Unity;
using Microsoft.Practices.Unity;
using Prism.Mvvm;
using DXVisualTestFixer.ViewModels;
using Prism.Regions;
using DXVisualTestFixer.Views;
using DevExpress.Xpf.Docking;
using DXVisualTestFixer.PrismCommon;
using DevExpress.Mvvm.UI;
using DevExpress.Xpf.Dialogs;
using DXVisualTestFixer.Core;

namespace DXVisualTestFixer {
    public class Bootstrapper : UnityBootstrapper {
        protected override DependencyObject CreateShell() {
            return Container.TryResolve(typeof(IShell)) as DependencyObject;
        }
        protected override void InitializeShell() {
            Application.Current.MainWindow.Show();
            Container.Resolve<IUpdateService>().Start();
        }

        protected override void ConfigureContainer() {
            base.ConfigureContainer();
            RegisterTypeIfMissing(typeof(IShell), typeof(Shell), true);
            RegisterTypeIfMissing(typeof(ILoggingService), typeof(LoggingService), true);
            RegisterTypeIfMissing(typeof(IVersionService), typeof(VersionService), true);
            RegisterTypeIfMissing(typeof(IMainViewModel), typeof(MainViewModel), true);
            RegisterTypeIfMissing(typeof(ISettingsViewModel), typeof(SettingsViewModel), false);
            RegisterTypeIfMissing(typeof(ITestInfoViewModel), typeof(TestInfoViewModel), false);
            RegisterTypeIfMissing(typeof(IFolderBrowserDialog), typeof(DXFolderBrowserDialog), false);
            RegisterTypeIfMissing(typeof(IAppearanceService), typeof(AppearanceService), true);
            RegisterTypeIfMissing(typeof(IUpdateService), typeof(UpdateService), true);
            RegisterTypeIfMissing(typeof(IFilterPanelViewModel), typeof(FilterPanelViewModel), false);
            RegisterTypeIfMissing(typeof(IApplyChangesViewModel), typeof(ApplyChangesViewModel), false);
            RegisterTypeIfMissing(typeof(IRepositoryOptimizerViewModel), typeof(RepositoryOptimizerViewModel), false);
            RegisterTypeIfMissing(typeof(IRepositoryAnalyzerViewModel), typeof(RepositoryAnalyzerViewModel), false);
            RegisterTypeIfMissing(typeof(IDXNotification), typeof(DXNotification), false);
            RegisterTypeIfMissing(typeof(IDXConfirmation), typeof(DXConfirmation), false);

            Container.RegisterTypeForNavigation<TestInfoView>();
            Container.RegisterTypeForNavigation<MergedTestInfoView>();
        }

        protected override RegionAdapterMappings ConfigureRegionAdapterMappings() {
            var mappings = base.ConfigureRegionAdapterMappings();
            mappings.RegisterMapping(typeof(LayoutPanel), Container.Resolve<LayoutPanelRegionAdapter>());
            return mappings;
        }

        protected override void ConfigureViewModelLocator() {
            base.ConfigureViewModelLocator();
            ViewModelLocationProvider.Register(typeof(MergedTestInfoView).FullName, typeof(ITestInfoViewModel));

            Container.Resolve<IRegionManager>().RegisterViewWithRegion(Regions.Regions.Main, typeof(MainView));
        }
    }
}
