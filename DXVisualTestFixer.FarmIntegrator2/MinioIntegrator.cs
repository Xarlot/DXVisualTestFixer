using DXVisualTestFixer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Minio;
using ThoughtWorks.CruiseControl.Remote;

namespace DXVisualTestFixer.FarmIntegrator2 {
    public class MinioIntegrator : IFarmIntegrator {
        async Task<List<IFarmTaskInfo>> IFarmIntegrator.GetAllTasks(Repository[] repositories) {
            var result = repositories.Select(repository => new FarmTaskInfo(repository, @"kamenchyuk.alexander/Common/19.2/")).Cast<IFarmTaskInfo>().ToList();
            return await Task.FromResult(result);
        }
    }
}