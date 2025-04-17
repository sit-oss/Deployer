using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deployer
{
    internal class DeployConfig
    {
        public string DownloadCenterName { get; set; }
        public DeployConfigItem[] Items { get; set; }
    }

    internal class DeployConfigItem
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public DeployConfigStep[] Check { get; set; }
        public DeployConfigStep[] Deploy { get; set; }
    }

    internal class DeployConfigStep
    {
        public string Action { get; set; }
        public string Target { get; set; }

        public async Task<bool> Perform()
        {
            return await Global.DoAction(Action, Target);
        }
    }
}
