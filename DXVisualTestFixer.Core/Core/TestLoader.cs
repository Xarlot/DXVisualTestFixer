using DXVisualTestFixer.Common;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml;

namespace DXVisualTestFixer.Core {
    public class CorpDirTestInfoContainer {
        public CorpDirTestInfoContainer(List<CorpDirTestInfo> failedTests, List<string> usedFilesLinks, List<IElapsedTimeInfo> elapsedTimes, List<Team> teams, (DateTime sources, DateTime tests)? buildTime) {
            FailedTests = failedTests;
            UsedFilesLinks = usedFilesLinks;
            ElapsedTimes = elapsedTimes;
            Teams = teams;
            SourcesBuildTime = buildTime?.sources;
            TestsBuildTime = buildTime?.tests;
        }

        public List<CorpDirTestInfo> FailedTests { get; }
        public List<string> UsedFilesLinks { get; }
        public List<IElapsedTimeInfo> ElapsedTimes { get; }
        public List<Team> Teams { get; }
        public DateTime? SourcesBuildTime { get; }
        public DateTime? TestsBuildTime { get; }
    }
    class ElapsedTimeInfo : IElapsedTimeInfo {
        public ElapsedTimeInfo(string name, TimeSpan time) {
            Name = name;
            Time = time;
        }

        public string Name { get; }
        public TimeSpan Time { get; }
    }

    public static class TestLoader {
        public static async Task<CorpDirTestInfoContainer> LoadFromInfo(IFarmTaskInfo taskInfo, string realUrl, IMinioWorker minioWorker) {
            List<CorpDirTestInfo> failedTests = new List<CorpDirTestInfo>();
            List<string> usedFileLinks = new List<string>();
            List<IElapsedTimeInfo> elapsedTimes = new List<IElapsedTimeInfo>();
            List<Team> teamsResult = new List<Team>();
            var pipelines = await minioWorker.Discover(realUrl);
            var lastPipeline = CalcLastPipeline(realUrl, pipelines);
            string resultsUrl = $@"{realUrl}{lastPipeline}/results/";
            var fail = await minioWorker.Download($@"{resultsUrl}fail");
            if (!string.IsNullOrEmpty(fail)) {
                var cases = await FindFailedTests(resultsUrl, minioWorker);
                foreach (var testCaseXml in cases) {
                    string testNameAndNamespace = testCaseXml.GetAttribute("name");
                    XmlNode failureNode = testCaseXml.FindByName("failure");
                    XmlNode resultNode = failureNode.FindByName("message");
                    XmlNode stackTraceNode = failureNode.FindByName("stack-trace");
                    List<CorpDirTestInfo> localRes = new List<CorpDirTestInfo>();
                    ParseMessage(taskInfo, testNameAndNamespace, resultNode.InnerText, stackTraceNode.InnerText, localRes);
                    failedTests.AddRange(localRes);
                }
                
                foreach (var ownerDoc in cases.Select(x => x.OwnerDocument).Distinct()) {
                    var links = FindUsedFilesLinks(ownerDoc);
                    if (links.Any())
                        usedFileLinks.AddRange(links);
                    var elapsed = FindElapsedTimes(ownerDoc);
                    if (elapsed.Any())
                        elapsedTimes.AddRange(elapsed);
                    var teams = FindTeams("19.2", ownerDoc);
                    if (teams.Any())
                        teamsResult.AddRange(teams);
                }
            }
            return new CorpDirTestInfoContainer(failedTests, usedFileLinks, elapsedTimes, teamsResult.Distinct().ToList(), null);
        }
        static string CalcLastPipeline(string rootUrl, string[] pipelines) {
            return pipelines.Select(x => x.Substring(rootUrl.Length, x.Length - rootUrl.Length - 1)).OrderByDescending(x => x).FirstOrDefault();
        }
        static XmlDocument LoadFromUrl(string realUrl) {
            XmlDocument myXmlDocument = new XmlDocument();
            realUrl = realUrl.Replace("ViewBuildReport.aspx", "XmlBuildLog.xml");
            int i = 0;
            while (i++ < 10) {
                try {
                    myXmlDocument.Load(realUrl);
                    return myXmlDocument;
                }
                catch {
                    if (i == 10)
                        throw;
                }
            }

            throw new NotSupportedException();
        }

        static bool IsSuccessBuild(XmlDocument myXmlDocument) {
            XmlNode buildNode = FindBuildNode(myXmlDocument);
            if (buildNode == null)
                return false;
            return !buildNode.TryGetAttibute("error", out string _);
        }
        static (DateTime sources, DateTime tests)? FindTimings(XmlDocument myXmlDocument) {
            XmlNode buildNode = FindBuildNode(myXmlDocument);
            if (buildNode == null)
                return null;
            var timingsNode = buildNode.FindByName("Timings");
            if (timingsNode == null)
                return null;
            try {
                return (GetDateTimeFromTimingsString(timingsNode.FindByName("Sources").InnerText), GetDateTimeFromTimingsString(timingsNode.FindByName("Tests").InnerText));
            }
            catch {
                return null;
            }
        }
        static DateTime GetDateTimeFromTimingsString(string dateStr) {
            var dateAndTime = dateStr.Split('_');
            var dateSplit = dateAndTime[0].Split('-');
            var timeSplit = dateAndTime[1].Split('-');
            return new DateTime(Convert.ToInt32(dateSplit[0]), Convert.ToInt32(dateSplit[1]), Convert.ToInt32(dateSplit[2]), Convert.ToInt32(timeSplit[0]), Convert.ToInt32(timeSplit[1]), 0);
        }

        static IEnumerable<XmlElement> FindFailedTests(XmlDocument myXmlDocument) {
            XmlNode buildNode = FindBuildNode(myXmlDocument);
            if (buildNode == null)
                yield break;
            foreach (var testResults in buildNode.FindAllByName("test-results")) {
                foreach (XmlElement subNode in FindAllFailedTests(testResults))
                    yield return subNode;
            }
        }
        static async Task<XmlElement[]> FindFailedTests(string url, IMinioWorker worker) {
            var result = new List<XmlElement>();
            var files = await worker.Discover(url);
            foreach (var file in files.Where(x => Path.GetExtension(x) == ".xml")) {
                var xmlDoc = new XmlDocument();
                var content = await worker.Download(file);
                xmlDoc.LoadXml($@"<cruisecontrol><build>{content}</build></cruisecontrol>");
                var failedTests = FindFailedTests(xmlDoc);
                if (failedTests.Any())
                    result.AddRange(failedTests);
            }

            return result.ToArray();
        }
        static IEnumerable<XmlElement> FindErrors(XmlDocument myXmlDocument) {
            XmlNode buildNode = FindBuildNode(myXmlDocument);
            if (buildNode == null)
                yield break;
            foreach (var root in buildNode.FindAllByName("root"))
                foreach (XmlElement error in root.FindAllByName("error"))
                    yield return error;
        }
        static IEnumerable<string> FindUsedFilesLinks(XmlDocument myXmlDocument) {
            XmlNode buildNode = FindBuildNode(myXmlDocument);
            if (buildNode == null)
                yield break;
            foreach (var usedFilesNode in buildNode.FindAllByName("FileUsingLogLink")) {
                yield return usedFilesNode.InnerText.Replace("\n", string.Empty);
            }
        }
        static List<IElapsedTimeInfo> FindElapsedTimes(XmlDocument myXmlDocument) {
            List<IElapsedTimeInfo> result = new List<IElapsedTimeInfo>();
            XmlNode buildNode = FindBuildNode(myXmlDocument);
            if (buildNode == null)
                return result;
            foreach (var elapsedTimeNode in buildNode.FindAllByName("ElapsedTime")) {
                string name;
                if (!elapsedTimeNode.TryGetAttibute("Name", out name))
                    continue;
                string time;
                if (!elapsedTimeNode.TryGetAttibute("Time", out time))
                    continue;
                if (int.TryParse(time.Split('.').FirstOrDefault() ?? time, out int sec))
                    result.Add(new ElapsedTimeInfo(name, TimeSpan.FromSeconds(sec)));
            }

            return result;
        }
        static List<Team> FindTeams(string version, XmlDocument myXmlDocument) {
            Dictionary<string, Team> result = new Dictionary<string, Team>();
            XmlNode buildNode = FindBuildNode(myXmlDocument);
            if (buildNode == null)
                return null;
            foreach (var teamNode in buildNode.FindAllByName("Project")) {
                int dpi;
                if (!teamNode.TryGetAttibute("Dpi", out dpi))
                    continue;
                string teamName;
                if (!teamNode.TryGetAttibute("IncludedCategories", out teamName))
                    continue;
                string resourcesFolder;
                if (!teamNode.TryGetAttibute("ResourcesFolder", out resourcesFolder))
                    continue;
                string testResourcesPath;
                if (!teamNode.TryGetAttibute("TestResourcesPath", out testResourcesPath))
                    continue;
                testResourcesPath = Path.Combine(resourcesFolder, testResourcesPath);
                string testResourcesPath_optimized = null;
                teamNode.TryGetAttibute("TestResourcesPath_Optimized", out testResourcesPath_optimized);
                if (testResourcesPath_optimized != null)
                    testResourcesPath_optimized = Path.Combine(resourcesFolder, testResourcesPath_optimized);
                var projectInfosNode = teamNode.FindByName("ProjectInfos");
                if (projectInfosNode == null)
                    continue;
                foreach (var projectInfoNode in projectInfosNode.FindAllByName("ProjectInfo")) {
                    string serverFolderName;
                    if (!projectInfoNode.TryGetAttibute("ServerFolderName", out serverFolderName))
                        continue;
                    bool optimized;
                    projectInfoNode.TryGetAttibute("Optimized", out optimized);
                    Team team;
                    if (!result.TryGetValue(teamName, out team))
                        result[teamName] = team = new Team() {Name = teamName, Version = version};
                    team.TeamInfos.Add(new TeamInfo()
                        {Dpi = dpi, Optimized = optimized, ServerFolderName = serverFolderName, TestResourcesPath = testResourcesPath, TestResourcesPath_Optimized = testResourcesPath_optimized});
                }
            }

            return result.Values.Count == 0 ? null : result.Values.ToList();
        }

        static bool TryGetAttibute<T>(this XmlNode node, string name, out T value) {
            value = default(T);
            var res = node.Attributes[name];
            if (res == null)
                return false;
            //value = (T)Convert.ChangeType(res, typeof(T));
            var converter = TypeDescriptor.GetConverter(typeof(T));
            value = (T)converter.ConvertFrom(res.Value);
            return true;
        }
        static XmlNode FindBuildNode(XmlDocument myXmlDocument) {
            return myXmlDocument.FindByName("cruisecontrol")?.FindByName("build");
        }
        static IEnumerable<XmlElement> FindAllFailedTests(XmlNode testResults) {
            foreach (XmlNode node in testResults.ChildNodes) {
                XmlElement xmlElement = node as XmlElement;
                if (xmlElement != null && xmlElement.Name == "test-case" && xmlElement.GetAttribute("success") == "False")
                    yield return xmlElement;
                else {
                    foreach (XmlElement subNode in FindAllFailedTests(node))
                        yield return subNode;
                }
            }
        }
        static XmlNode FindByName(this XmlNode element, string name) {
            foreach (XmlNode node in element.ChildNodes) {
                if (node.Name == name)
                    return node;
            }

            return null;
        }
        static IEnumerable<XmlNode> FindAllByName(this XmlNode element, string name) {
            foreach (XmlNode node in element.ChildNodes) {
                if (node.Name == name)
                    yield return node;
            }
        }
        public static void ParseMessage(IFarmTaskInfo farmTaskInfo, string testNameAndNamespace, string message, string stackTrace, List<CorpDirTestInfo> resultList) {
            if (!message.StartsWith("Exception - NUnit.Framework.AssertionException")) {
                resultList.Add(CorpDirTestInfo.CreateError(farmTaskInfo, testNameAndNamespace, message, stackTrace));
                return;
            }

            List<string> themedResultPaths = message.Split(new[] {" - failed:"}, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (themedResultPaths.Count == 1) {
                resultList.Add(CorpDirTestInfo.CreateError(farmTaskInfo, testNameAndNamespace, message, stackTrace));
                return;
            }

            foreach (var part in themedResultPaths) {
                ParseMessagePart(farmTaskInfo, testNameAndNamespace, part, resultList);
            }
        }
        static void ParseMessagePart(IFarmTaskInfo farmTaskInfo, string testNameAndNamespace, string message, List<CorpDirTestInfo> resultList) {
            List<string> parts = message.Split(new[] {"\n"}, StringSplitOptions.RemoveEmptyEntries).ToList();
            List<string> paths = parts.Where(x => x.Contains(@"\\corp")).Select(x => x.Replace(@"\\corp", String.Empty)).ToList();

            List<string> resultPaths = PatchPaths(paths);
            List<string> shaList = PatchSHA(parts.Where(x => x.Contains("sha-")).Select(x => x.Replace("sha-", String.Empty)).ToList());
            int? diffCount = TryGetDiffCount(parts.Where(x => x.Contains("diffCount=")).Select(x => x.Replace("diffCount=", String.Empty)).LastOrDefault());
            CorpDirTestInfo info = null;
            if (!CorpDirTestInfo.TryCreate(farmTaskInfo, testNameAndNamespace, resultPaths, shaList, diffCount, out info))
                return;
            resultList.Add(info);
        }
        static int? TryGetDiffCount(string str) {
            if (Int32.TryParse(str, out int res))
                return res;
            return null;
        }
        static List<string> PatchSHA(List<string> shaList) {
            var result = new List<string>();
            foreach (var sha in shaList) {
                result.Add(sha.Replace("\r", String.Empty).Replace("\n", String.Empty));
            }

            return result;
        }
        static List<string> PatchPaths(List<string> resultPaths) {
            List<string> result = new List<string>();
            foreach (var pathCandidate in resultPaths) {
                if (!pathCandidate.Contains('\\'))
                    continue;
                string cleanPath = @"\\corp" + pathCandidate.Replace("\r", String.Empty).Replace("\n", String.Empty).Replace(@"\\", @"\");
                if (cleanPath.Contains(' '))
                    continue;
                if (File.Exists(cleanPath)) {
                    result.Add(cleanPath);
                    continue;
                }

                if (cleanPath.Contains("InstantBitmap.png.sha")) {
                    SafeAddPath("InstantBitmap.png.sha", cleanPath, result);
                    continue;
                }

                if (cleanPath.Contains("InstantBitmap.png")) {
                    SafeAddPath("InstantBitmap.png", cleanPath, result);
                    continue;
                }

                if (cleanPath.Contains("BitmapDif.png")) {
                    SafeAddPath("BitmapDif.png", cleanPath, result);
                    continue;
                }

                if (cleanPath.Contains("CurrentBitmap.png.sha")) {
                    SafeAddPath("CurrentBitmap.png.sha", cleanPath, result);
                    continue;
                }

                if (cleanPath.Contains("CurrentBitmap.png")) {
                    SafeAddPath("CurrentBitmap.png", cleanPath, result);
                    continue;
                }
            }

            return result;
        }
        static void SafeAddPath(string fileName, string pathCandidate, List<string> paths) {
            var cleanPath = pathCandidate.Split(new[] {fileName}, StringSplitOptions.RemoveEmptyEntries).First() + fileName;
            if (File.Exists(cleanPath))
                paths.Add(cleanPath);
        }
    }
}