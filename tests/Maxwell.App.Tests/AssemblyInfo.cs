using Xunit.v3;

// One test at a time, for one reason: Avalonia's dispatcher is a process-wide singleton
// bound to whichever thread touched it first. Several tests here build view models, and a
// view model posts its settle to that dispatcher; the ones that have to pump it take
// ownership first. Run in parallel, one test rebinds the dispatcher while another is
// pumping it and the second fails with a cross-thread access error that has nothing to do
// with what it was testing. The whole assembly runs in a fifth of a second, so serialising
// it costs nothing worth measuring.
[assembly: Parallelization(MaxThreads = 1)]
