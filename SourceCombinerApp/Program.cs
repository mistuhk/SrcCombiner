var result = SourceCombiner.CombineSourceFiles(
    sourceDirectory: "/Users/mistuhkay/Downloads/webapp",
    outputFilePath:  "/Users/mistuhkay/Downloads/webapp-src.txt",
    useRelativePaths: true
);

result.PrintSummary();