﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using GoogleTestAdapter.Helpers;

namespace GoogleTestAdapter.Runners
{

    class CommandLineGenerator
    {
        internal class Args
        {
            internal List<TestCase> TestCases { get; }
            internal string CommandLine { get; }

            internal Args(List<TestCase> testCases, string commandLine)
            {
                this.TestCases = testCases ?? new List<TestCase>();
                this.CommandLine = commandLine ?? "";
            }
        }

        internal const int MaxCommandLength = 8191;

        private int LengthOfExecutableString { get; }
        private IEnumerable<TestCase> AllCases { get; }
        private IEnumerable<TestCase> CasesToRun { get; }
        private string ResultXmlFile { get; }
        private TestEnvironment TestEnvironment { get; }
        private string UserParameters { get; }

        internal CommandLineGenerator(
            IEnumerable<TestCase> allCases, IEnumerable<TestCase> casesToRun,
            int lengthOfExecutableString, string userParameters, string resultXmlFile,
            TestEnvironment testEnvironment)
        {
            if (userParameters == null)
            {
                throw new ArgumentNullException(nameof(userParameters));
            }

            this.LengthOfExecutableString = lengthOfExecutableString;
            this.AllCases = allCases;
            this.CasesToRun = casesToRun;
            this.ResultXmlFile = resultXmlFile;
            this.TestEnvironment = testEnvironment;
            this.UserParameters = userParameters;
        }

        internal IEnumerable<Args> GetCommandLines()
        {
            string baseCommandLine = GetOutputpathParameter();
            baseCommandLine += GetAlsoRunDisabledTestsParameter();
            baseCommandLine += GetShuffleTestsParameter();
            baseCommandLine += GetTestsRepetitionsParameter();

            List<Args> commandLines = new List<Args>();
            commandLines.AddRange(GetFinalCommandLines(baseCommandLine));
            return commandLines;
        }

        private IEnumerable<Args> GetFinalCommandLines(string baseCommandLine)
        {
            List<Args> commandLines = new List<Args>();
            string userParam = GetAdditionalUserParameter();
            if (AllTestCasesOfExecutableAreRun())
            {
                commandLines.Add(new Args(CasesToRun.ToList(), baseCommandLine + userParam));
                return commandLines;
            }

            List<string> suitesRunningAllTests = GetSuitesRunningAllTests();
            string baseFilter =
                GoogleTestConstants.FilterOption + GetFilterForSuitesRunningAllTests(suitesRunningAllTests);
            string baseCommandLineWithFilter = baseCommandLine + baseFilter;

            List<TestCase> testsNotRunBySuite = GetCasesNotRunBySuite(suitesRunningAllTests);
            List<TestCase> testsRunBySuite = CasesToRun.Where(tc => !testsNotRunBySuite.Contains(tc)).ToList();
            if (testsNotRunBySuite.Count == 0)
            {
                commandLines.Add(new Args(CasesToRun.ToList(), baseCommandLineWithFilter + userParam));
                return commandLines;
            }

            List<TestCase> includedTestCases;
            string commandLine = baseCommandLineWithFilter +
                                 JoinTestsUpToMaxLength(testsNotRunBySuite,
                                     MaxCommandLength - baseCommandLineWithFilter.Length - LengthOfExecutableString - userParam.Length - 1,
                                     out includedTestCases);
            includedTestCases.AddRange(testsRunBySuite);
            commandLines.Add(new Args(includedTestCases, commandLine + userParam));
            baseCommandLineWithFilter = baseCommandLine + GoogleTestConstants.FilterOption; // only add suites to first command line

            while (testsNotRunBySuite.Count > 0)
            {
                commandLine = baseCommandLineWithFilter +
                              JoinTestsUpToMaxLength(testsNotRunBySuite,
                                  MaxCommandLength - baseCommandLineWithFilter.Length - LengthOfExecutableString - userParam.Length - 1,
                                  out includedTestCases);
                commandLines.Add(new Args(includedTestCases, commandLine + userParam));
            }

            return commandLines;
        }

        private string JoinTestsUpToMaxLength(List<TestCase> tests, int maxLength, out List<TestCase> includedTestCases)
        {
            includedTestCases = new List<TestCase>();
            if (tests.Count == 0)
            {
                return "";
            }

            string result = "";
            string nextTest = GetTestcaseNameForFiltering(tests[0].FullyQualifiedName);
            if (nextTest.Length > maxLength)
            {
                throw new Exception("CommandLineGenerator: I can not deal with this case :-( - maxLength=" + maxLength +
                    ", includedTestCases.Count=" + includedTestCases.Count + ", nextTest.Length=" + nextTest.Length);
            }

            while (result.Length + nextTest.Length <= maxLength && tests.Count > 0)
            {
                result += nextTest;
                includedTestCases.Add(tests[0]);
                tests.RemoveAt(0);
                if (tests.Count > 0)
                {
                    nextTest = ":" + GetTestcaseNameForFiltering(tests[0].FullyQualifiedName);
                }
            }
            return result;
        }

        private string GetAdditionalUserParameter()
        {
            return UserParameters.Length == 0 ? "" : " " + UserParameters;
        }

        private string GetOutputpathParameter()
        {
            return GoogleTestConstants.GetResultXmlFileOption(ResultXmlFile);
        }

        private string GetAlsoRunDisabledTestsParameter()
        {
            return TestEnvironment.Options.RunDisabledTests ? GoogleTestConstants.AlsoRunDisabledTestsOption : "";
        }

        private string GetShuffleTestsParameter()
        {
            return TestEnvironment.Options.ShuffleTests ? GoogleTestConstants.ShuffleTestsOption : "";
        }

        private string GetTestsRepetitionsParameter()
        {
            int nrOfRepetitions = TestEnvironment.Options.NrOfTestRepetitions;
            if (nrOfRepetitions == 1)
            {
                return "";
            }
            return GoogleTestConstants.NrOfRepetitionsOption + nrOfRepetitions;
        }

        private string GetFilterForSuitesRunningAllTests(List<string> suitesRunningAllTests)
        {
            return string.Join(".*:", suitesRunningAllTests).AppendIfNotEmpty(".*:");
        }

        private bool AllTestCasesOfExecutableAreRun()
        {
            HashSet<TestCase> allCasesSet = new HashSet<TestCase>(AllCases);
            HashSet<TestCase> casesToRunSet = new HashSet<TestCase>(CasesToRun);
            return allCasesSet.SetEquals(casesToRunSet);
        }

        private List<TestCase> GetCasesNotRunBySuite(List<string> suitesRunningAllTests)
        {
            List<TestCase> casesNotRunBySuite = new List<TestCase>();
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (TestCase testCase in CasesToRun)
            {
                bool isRunBySuite = suitesRunningAllTests.Any(s => s == GetTestsuiteNameFromCase(testCase));
                if (!isRunBySuite)
                {
                    casesNotRunBySuite.Add(testCase);
                }
            }
            return casesNotRunBySuite;
        }

        private List<string> GetSuitesRunningAllTests()
        {
            List<string> suitesRunningAllTests = new List<string>();
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (string suite in GetAllSuitesOfTestCasesToRun())
            {
                List<TestCase> allMatchingCasesToBeRun = GetAllMatchingCases(CasesToRun, suite);
                List<TestCase> allMatchingCases = GetAllMatchingCases(AllCases, suite);
                if (allMatchingCasesToBeRun.Count == allMatchingCases.Count)
                {
                    suitesRunningAllTests.Add(suite);
                }
            }
            return suitesRunningAllTests;
        }

        private List<string> GetAllSuitesOfTestCasesToRun()
        {
            return CasesToRun.Select(GetTestsuiteNameFromCase).Distinct().ToList();
        }

        private List<TestCase> GetAllMatchingCases(IEnumerable<TestCase> cases, string suite)
        {
            return cases.Where(testcase => suite == GetTestsuiteNameFromCase(testcase)).ToList();
        }

        private string GetTestsuiteNameFromCase(TestCase testcase)
        {
            return testcase.FullyQualifiedName.Split('.')[0];
        }

        private string GetTestcaseNameForFiltering(string fullname)
        {
            int index = fullname.IndexOf(' ');
            if (index < 0)
            {
                return fullname;
            }
            return fullname.Substring(0, index);
        }

    }

}
