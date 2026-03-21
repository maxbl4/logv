// UI tests launch a real process and interact via UIAutomation + SendKeys.
// Running multiple instances in parallel causes COMExceptions and focus races.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
