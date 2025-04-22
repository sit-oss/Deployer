using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Deployer
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<SoftwareItem> SoftwareList { get; } = new ObservableCollection<SoftwareItem>();

        public string UserName => System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\').Last();

        public IAsyncCommand<SoftwareItem> DeployCommand { get; }

        private bool _isReady = true;
        public bool IsReady
        {
            get => _isReady;
            set { _isReady = value; OnPropertyChanged(); }
        }

        private string _title = "Deployer";
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public MainViewModel()
        {
            DeployCommand = new AsyncCommand<SoftwareItem>(DeploySoftware);
            _ = Init();
        }

        private async Task Init()
        {
            IsReady = false;
            await Global.LoadConfigs();
            if (Global.Config == null) return;
            Title = $"ダウンロード源：{Global.Config.DownloadCenterName} | {UserName}";
            foreach (var item in Global.Config.Items)
            {
                var isChecked = true;
                foreach (var check in item.Check)
                {
                    var perform = await check.Perform();
                    if (perform) continue;
                    isChecked = false;
                    break;
                }
                SoftwareList.Add(new SoftwareItem { Name = item.Name, Version = item.Version, IsDeployed = isChecked });
            }
            IsReady = true;
        }

        private async Task DeploySoftware(SoftwareItem software)
        {
            try
            {
                if (software == null || software.IsDeployed) return;
                var item = Global.Config.Items.Single(x => x.Name == software.Name);
                IsReady = false;
                foreach (var action in item.Deploy)
                {
                    var perform = await action.Perform();
                    if (!perform) break;
                }
            
                var isChecked = true;
                foreach (var check in item.Check)
                {
                    var perform = await check.Perform();
                    if (perform) continue;
                    isChecked = false;
                    break;
                }
                software.IsDeployed = isChecked;
            }
            catch (Exception e)
            {
                //TODO
            }
            finally
            {
                IsReady = true;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
