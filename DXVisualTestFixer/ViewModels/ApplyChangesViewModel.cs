﻿using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using DXVisualTestFixer.Core;
using Microsoft.Practices.Unity;
using Prism.Interactivity.InteractionRequest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DXVisualTestFixer.ViewModels {
    public interface IApplyChangesViewModel : IConfirmation { }

    public class ApplyChangesViewModel : ViewModelBase, IApplyChangesViewModel {
        public IEnumerable<UICommand> DialogCommands { get; private set; }

        public List<TestInfoWrapper> ChangedTests { get; }

        public bool Confirmed { get; set; }
        public string Title { get;  set; }
        public object Content { get; set; }

        public IEnumerable<UICommand> Commands { get; }

        public ApplyChangesViewModel(IUnityContainer container) {
            Title = "Settings";
            Commands = UICommand.GenerateFromMessageButton(MessageButton.OKCancel, new DialogService(), MessageResult.OK, MessageResult.Cancel);
            Commands.Where(c => c.IsDefault).Single().Command = new DelegateCommand(() => Confirmed = true);
            ChangedTests = container.Resolve<IMainViewModel>().GetChangedTests();
        }
    }
}
