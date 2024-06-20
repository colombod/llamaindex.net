﻿using LlamaIndex.Core.Schema;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace LlamaParse;

public static class LlamaParseExtensions
{
    public static IAsyncEnumerable<Document> LoadDataAsync(this LlamaParseClient llamaParseClient, FileInfo file, bool splitByPage = false, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
    {
        return llamaParseClient.LoadDataAsync([file], splitByPage, metadata, cancellationToken);
    }

    public static IAsyncEnumerable<Document> LoadDataAsync(this LlamaParseClient llamaParseClient, InMemoryFile inMemoryFile, bool splitByPage = false, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
    {
        return llamaParseClient.LoadDataAsync([inMemoryFile], splitByPage, metadata, cancellationToken);
    }


    public static async IAsyncEnumerable<Document> LoadDataAsync(
        this LlamaParseClient llamaParseClient,
        IEnumerable<InMemoryFile> inMemoryFiles,
        bool splitByPage = false,
        Dictionary<string, object>? metadata = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var documentMetadata = metadata ?? new Dictionary<string, object>();

        await foreach (var rawResult in llamaParseClient.LoadDataRawAsync(inMemoryFiles, ResultType.Json, documentMetadata, cancellationToken))
        {
            await foreach (var document in CreateDocumentsFromRawResult(llamaParseClient, rawResult, splitByPage, documentMetadata, cancellationToken))
            {
                yield return document;
            }
        }

    }

    public static async IAsyncEnumerable<Document> LoadDataAsync(this LlamaParseClient llamaParseClient, IEnumerable<FileInfo> files, bool splitByPage = false, Dictionary<string, object>? metadata = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var documentMetadata = metadata ?? new Dictionary<string, object>();

        await foreach (var rawResult in llamaParseClient.LoadDataRawAsync(files, ResultType.Json, documentMetadata, cancellationToken))
        {
            await foreach (var document in CreateDocumentsFromRawResult(llamaParseClient, rawResult, splitByPage, documentMetadata, cancellationToken))
            {
                yield return document;
            }
        }
    }

    private static async IAsyncEnumerable<Document> CreateDocumentsFromRawResult(LlamaParseClient llamaParseClient,
        RawResult rawResult, bool splitByPage,
        Dictionary<string, object> documentMetadata,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jobId = rawResult.JobId;

        var result = rawResult.Result;

        var documentByPage = new Dictionary<int, RelatedNodeInfo>();

        if (splitByPage)
        {
            foreach (var page in result.GetProperty("pages").EnumerateArray())
            {
                var pageNumber = page.GetProperty("page").GetInt32();
                var pageMetadata = new Dictionary<string, object>(documentMetadata)
                {
                    ["page_number"] = pageNumber
                };

                Document? document = null;
                switch (llamaParseClient.Configuration.ResultType)
                {
                    case ResultType.Markdown:
                        if (page.TryGetProperty("md", out var markdown))
                        {
                            document = new Document(
                                id:Guid.NewGuid().ToString(), 
                                text:markdown.GetString(),
                                mimeType: "text/markdown",
                                metadata:pageMetadata);
                        }

                        break;
                    case ResultType.Text:
                        if (page.TryGetProperty("text", out var text))
                        {
                            document = new  Document(
                                id:Guid.NewGuid().ToString(), 
                                text:text.GetString(),
                                mimeType: "text/plain",
                                metadata:pageMetadata);
                        }

                        break;
                    case ResultType.Json:
                        document = new Document(
                            id:Guid.NewGuid().ToString(), 
                            text:page.GetRawText(),
                            mimeType: "application/json",
                            metadata: pageMetadata);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (document is not null)
                {
                    documentByPage[pageNumber] = new RelatedNodeInfo(document.Id, NodeType.Document, pageMetadata);
                    yield return document;
                }
            }
        }
        else
        {
            documentByPage[-1] = new RelatedNodeInfo(jobId, NodeType.Document, documentMetadata);

            if (llamaParseClient.Configuration.ResultType == ResultType.Json)
            {
               
                yield return new Document(
                    id:jobId, 
                    text:result.GetRawText(),
                    mimeType: "application/json",
                    metadata: documentMetadata);
            }

            var mimeType = llamaParseClient.Configuration.ResultType switch
            {
                ResultType.Markdown => "text/markdown",
                ResultType.Text => "text/plain",
                _ => throw new ArgumentOutOfRangeException()
            };

            var content = new StringBuilder();
            foreach (var page in result.GetProperty("pages").EnumerateArray())
            {
                
                switch (llamaParseClient.Configuration.ResultType)
                {
                    case ResultType.Markdown:
                        if (page.TryGetProperty("md", out var markdown))
                        {
                            content.AppendLine(markdown.GetString());
                        }

                        break;
                    case ResultType.Text:
                        if (page.TryGetProperty("text", out var text))
                        {
                            content.AppendLine(text.GetString());
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            yield return new Document(
                id:jobId, 
                text:content.ToString(),
                mimeType: mimeType,
                metadata:documentMetadata);
        }

        if (llamaParseClient.Configuration.ExtractImages)
        {
            await foreach (var image in llamaParseClient.LoadImagesAsync(jobId, documentMetadata, cancellationToken))
            {
                if (documentByPage.Count > 0)
                {
                    image.ParentNode =
                        documentByPage.TryGetValue((int)image.Metadata["page_number"], out var nodeReference)
                            ? nodeReference
                            : documentByPage[-1];
                }

                yield return image;
            }
        }
    }
}