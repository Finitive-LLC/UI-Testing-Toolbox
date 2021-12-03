using Atata;
using Lombiq.Tests.UI.Extensions;
using Lombiq.Tests.UI.Services;
using OpenQA.Selenium.Remote;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Lombiq.Tests.UI.MonkeyTesting
{
    public sealed class MonkeyTester
    {
        private const string SetIsMonkeyTestRunningScript = "window.isMonkeyTestRunning = true;";

        private const string GetIsMonkeyTestRunningScript = "return !!window.isMonkeyTestRunning;";

        private readonly UITestContext _context;

        private readonly MonkeyTestingOptions _options;

        private readonly Random _random;

        private readonly List<PageMonkeyTestInfo> _pageTestInfoList = new();

        public MonkeyTester(UITestContext context, MonkeyTestingOptions options = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _options = options ?? new MonkeyTestingOptions();
            _random = new Random(_options.BaseRandonSeed);
        }

        private ILogManager Log => _context.Scope.AtataContext.Log;

        public void Test() =>
            Log.ExecuteSection(
                new LogSection($"Execute monkey testing"),
                () =>
                {
                    var pageTestInfo = GetCurrentPageTestInfo();
                    TestCurrentPage(pageTestInfo);

                    while (true)
                    {
                        pageTestInfo = GetCurrentPageTestInfo();

                        if (CanTestPage(pageTestInfo))
                        {
                            TestCurrentPage(pageTestInfo);
                        }
                        else if (TryGetLeftPageToTest(out var leftPageToTest))
                        {
                            _context.Scope.AtataContext.Go.ToUrl(leftPageToTest.Url);

                            TestCurrentPage(leftPageToTest);
                        }
                        else
                        {
                            return;
                        }
                    }
                });

        private bool CanTestPage(PageMonkeyTestInfo pageTestInfo)
        {
            bool canTest = pageTestInfo.HasTimeToTest && ShouldTestPageUrl(pageTestInfo.Url);

            if (!canTest)
            {
                Log.Info(
                    !pageTestInfo.HasTimeToTest
                    ? $"\"{pageTestInfo.CleanUrl}\" is tested completely"
                    : $"Navigated to \"{pageTestInfo.Url}\" that should not be tested");
            }

            return canTest;
        }

        private bool ShouldTestPageUrl(string url) =>
            _options.UrlFilters.All(filter => filter.CanHandle(url, _context));

        private bool TryGetLeftPageToTest(out PageMonkeyTestInfo pageTestInfo)
        {
            pageTestInfo = _pageTestInfoList.FirstOrDefault(x => x.HasTimeToTest);
            return pageTestInfo != null;
        }

        private PageMonkeyTestInfo GetCurrentPageTestInfo()
        {
            var url = _context.Driver.Url;
            var cleanUrl = CleanUrl(url);

            var pageTestInfo = _pageTestInfoList.FirstOrDefault(x => x.CleanUrl == cleanUrl)
                ?? new PageMonkeyTestInfo(url, cleanUrl, _options.PageTestTime);

            Log.Info($"Current page is \"{pageTestInfo.CleanUrl}\"");

            return pageTestInfo;
        }

        private string CleanUrl(string url)
        {
            foreach (var cleaner in _options.UrlCleaners)
                url = cleaner.Handle(url, _context);

            return url;
        }

        private void TestCurrentPage(PageMonkeyTestInfo pageTestInfo)
        {
            int randomSeed = GetRandomSeed();

            Log.ExecuteSection(
                new LogSection(
#pragma warning disable S103 // Lines should not be too long
                    $"Monkey test \"{pageTestInfo.CleanUrl}\" within {pageTestInfo.TimeToTest.ToShortIntervalString()} with {randomSeed} random seed"),
#pragma warning restore S103 // Lines should not be too long
                () =>
                {
                    var pageTestTimeLeft = TestCurrentPageAndMeasureTestTimeLeft(pageTestInfo.TimeToTest, randomSeed);
                    pageTestInfo.TimeToTest = pageTestTimeLeft;
                    if (!_pageTestInfoList.Contains(pageTestInfo))
                        _pageTestInfoList.Add(pageTestInfo);
                });
        }

        [SuppressMessage("Security", "SCS0005:Weak random number generator.", Justification = "For current purpose it should not be secured.")]
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "For current purpose it should not be secured.")]
        private int GetRandomSeed() =>
            _random.Next();

        private TimeSpan TestCurrentPageAndMeasureTestTimeLeft(TimeSpan testTime, int randomSeed)
        {
            if (_options.RunAccessibilityCheckingAssertion)
                _context.AssertAccessibility();

            if (_options.RunHtmlValidationEnabledAssertion)
                _context.AssertHtmlValidityAsync().GetAwaiter().GetResult();

            _context.Driver.ExecuteScript(SetIsMonkeyTestRunningScript);

            string gremlinsScript = BuildGremlinsScript(testTime, randomSeed);
            _context.Driver.ExecuteScript(gremlinsScript);

            return MeasureTimeLeftOfMeetingPredicate(
                _context.Driver,
                driver => !(bool)driver.ExecuteScript(GetIsMonkeyTestRunningScript),
                timeout: testTime,
                pollingInterval: _options.PageMarkerPollingInterval);
        }

        private string BuildGremlinsScript(TimeSpan testTime, int randomSeed) =>
            new GremlinsScriptBuilder
            {
                Species = _options.GremlinsSpecies.ToArray(),
                Mogwais = _options.GremlinsMogwais.ToArray(),
                NumberOfAttacks = (int)(testTime.TotalMilliseconds / _options.GremlinsAttackDelay.TotalMilliseconds),
                AttackDelay = (int)_options.GremlinsAttackDelay.TotalMilliseconds,
                RandomSeed = randomSeed,
            }
            .Build();

        private static TimeSpan MeasureTimeLeftOfMeetingPredicate(
            RemoteWebDriver webDriver,
            Func<RemoteWebDriver, bool> predicate,
            TimeSpan timeout,
            TimeSpan pollingInterval)
        {
            var wait = new SafeWait<RemoteWebDriver>(webDriver)
            {
                Timeout = timeout,
                PollingInterval = pollingInterval,
            };

            var stopwatch = Stopwatch.StartNew();
            var isPageInterrupted = wait.Until(predicate);
            stopwatch.Stop();

            if (isPageInterrupted)
            {
                var timeLeft = timeout - stopwatch.Elapsed;
                return timeLeft > TimeSpan.Zero ? timeLeft : TimeSpan.Zero;
            }
            else
            {
                return TimeSpan.Zero;
            }
        }
    }
}