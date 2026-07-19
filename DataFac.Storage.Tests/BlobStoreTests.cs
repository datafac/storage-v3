using DataFac.Compression;
using DataFac.Hashing;
using PublicApiGenerator;
using Shouldly;
using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

#pragma warning disable CA1707 // Identifiers should not contain underscores
#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task

namespace DataFac.Storage.Tests;

public class BlobStoreTests
{
    private const string testroot = @"C:\temp\unittest\RocksDB\";

    [Theory]
    [InlineData(StoreKind.Testing)]
#if NET8_0_OR_GREATER
    [InlineData(StoreKind.RocksDb)]
#endif
    public void Store01Create(StoreKind storeKind)
    {
        string testpath = $"{testroot}{Guid.NewGuid():N}";
        using IDataStore dataStore = TestHelpers.CreateDataStore(storeKind, testpath);

        var counters = dataStore.GetCounters();
        counters.BlobPutCount.ShouldBe(0);
        counters.BlobPutWrits.ShouldBe(0);
        counters.BlobPutSkips.ShouldBe(0);
        counters.ByteDelta.ShouldBe(0);
    }

    [Theory]
    [InlineData(StoreKind.Testing)]
#if NET8_0_OR_GREATER
    [InlineData(StoreKind.RocksDb)]
#endif
    public async Task Store02GetEmptyIdReturnsNull(StoreKind storeKind)
    {
        string testpath = $"{testroot}{Guid.NewGuid():N}";
        using IDataStore dataStore = TestHelpers.CreateDataStore(storeKind, testpath);

        var result = await dataStore.GetBlob(default);
        result.HasValue.ShouldBeFalse();
    }

    [Theory]
    [InlineData(StoreKind.Testing)]
#if NET8_0_OR_GREATER
    [InlineData(StoreKind.RocksDb)]
#endif
    public async Task Store03GetInvalidId(StoreKind storeKind)
    {
        string testpath = $"{testroot}{Guid.NewGuid():N}";
        using IDataStore dataStore = TestHelpers.CreateDataStore(storeKind, testpath);

        BlobData data = BlobData.From(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
        Memory<byte> idMemory = new byte[BlobIdV1.Size];
        BlobHelpers.CompressData(data.Bytes, idMemory.Span);
        BlobKey key = BlobKey.From(idMemory);
        var result = await dataStore.GetBlob(key);
        result.HasValue.ShouldBeFalse();
        var counters = dataStore.GetCounters();
        counters.BlobGetCount.ShouldBe(1);
        counters.BlobGetReads.ShouldBe(1);
        counters.BlobGetCache.ShouldBe(0);
    }

    [Theory]
    [InlineData(StoreKind.Testing)]
#if NET8_0_OR_GREATER
    [InlineData(StoreKind.RocksDb)]
#endif
    public async Task Store04PutNonEmptyBlob(StoreKind storeKind)
    {
        string testpath = $"{testroot}{Guid.NewGuid():N}";
        using IDataStore dataStore = TestHelpers.CreateDataStore(storeKind, testpath);

        BlobData data = BlobData.From(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        Memory<byte> idMemory = new byte[BlobIdV1.Size];
        (bool embedded, var compressed) = BlobHelpers.CompressData(data.Bytes, idMemory.Span);
        embedded.ShouldBeFalse();
        ReadOnlySpan<byte> idSpan= idMemory.Span;

        (_,_, var compAlgo, var hashAlgo, _) = BlobIdV1.ReadNonEmbedded(idSpan);
        hashAlgo.ShouldBe(BlobHashAlgo.Sha256);
        compAlgo.ShouldBe(BlobCompAlgo.UnComp);
        BlobIdV1.ToDisplayString(idSpan).ShouldBe("V1.0:256:U:S:QK/y6dLYki5Hr9RkjmlnSXFYeF+9Hahw5xECZr+USIA=");

        BlobKey key = BlobKey.From(idMemory);
        await dataStore.PutBlob(key, data, true);
        var counters = dataStore.GetCounters();
        counters.BlobPutCount.ShouldBe(1);
        counters.BlobPutWrits.ShouldBe(1);
        counters.BlobPutSkips.ShouldBe(0);
    }

    [Theory]
    [InlineData(StoreKind.Testing)]
#if NET8_0_OR_GREATER
    [InlineData(StoreKind.RocksDb)]
#endif
    public async Task Store05GetCompressed(StoreKind storeKind)
    {
        string testpath = $"{testroot}{Guid.NewGuid():N}";
        using IDataStore dataStore = TestHelpers.CreateDataStore(storeKind, testpath);

        var text =
            "The rain in Spain falls mainly on the plain. " +
            "Please explain my pain and disdain or I will go insain [sic]. " +
            "Plain Jain is a brain in a train in Spain. " +
            "Maine is the main domain to obtain the brain drain.";

        BlobKey key;
        {
            // sender
            Memory<byte> idMemory = new byte[BlobIdV1.Size];
            var idSpan = idMemory.Span;
            (var _, var compressed) = BlobHelpers.CompressText(text, idSpan);
            key = BlobKey.From(idMemory);
            BlobData data = BlobData.From(compressed);

            (_, _, var compAlgo, var hashAlgo, _) = BlobIdV1.ReadNonEmbedded(idSpan);
            hashAlgo.ShouldBe(BlobHashAlgo.Sha256);
            compAlgo.ShouldBe(BlobCompAlgo.Snappy);
            BlobIdV1.ToDisplayString(idSpan).ShouldBe("V1.0:201:S:S:f+8O2Wm1is/9ut73eja0VCML3qUOWA9rgBZg4INPL34=");

            await dataStore.PutBlob(key, data, true);
        }

        {
            // recver
            var recd = await dataStore.GetBlob(key);
            recd.HasValue.ShouldBeTrue();

            //(bool embedded, var data) = BlobHelpers.TryGetEmbedded(key.Bytes);
            //data.HasValue.ShouldBeFalse();
            //embedded.ShouldBeFalse();

            var copy = BlobHelpers.DecompressData(key.Bytes.Span, recd.Bytes);
            string text2 = Encoding.UTF8.GetString(copy.ToArray());
            text2.ShouldBe(text);
        }

        var counters = dataStore.GetCounters();
        counters.BlobPutCount.ShouldBe(1);
        counters.BlobPutWrits.ShouldBe(1);
        counters.BlobPutSkips.ShouldBe(0);
        counters.BlobGetCount.ShouldBe(1);
        counters.BlobGetCache.ShouldBe(1);
        counters.BlobGetReads.ShouldBe(0);
    }

    [Theory]
    [InlineData(StoreKind.Testing)]
#if NET8_0_OR_GREATER
    [InlineData(StoreKind.RocksDb)]
#endif
    public async Task Store06GetUncompressed(StoreKind storeKind)
    {
        string testpath = $"{testroot}{Guid.NewGuid():N}";
        using IDataStore dataStore = TestHelpers.CreateDataStore(storeKind, testpath);

        BlobData data = BlobData.From(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        BlobKey key;
        {
            // sender
            Memory<byte> idMemory = new byte[BlobIdV1.Size];
            BlobHelpers.CompressData(data.Bytes, idMemory.Span);
            key = BlobKey.From(idMemory);

            await dataStore.PutBlob(key, data, true);
        }

        {
            // recver
            //(bool embedded, _) = BlobHelpers.TryGetEmbedded(key.Bytes);
            //embedded.ShouldBeFalse();

            (_, _, var compAlgo, var hashAlgo, _) = BlobIdV1.ReadNonEmbedded(key.Bytes.Span);
            compAlgo.ShouldBe(BlobCompAlgo.UnComp);

            var copy = await dataStore.GetBlob(key);
            copy.HasValue.ShouldBeTrue();
            copy.Bytes.Span.SequenceEqual(data.Bytes.Span).ShouldBeTrue();
        }

        var counters = dataStore.GetCounters();
        counters.BlobPutCount.ShouldBe(1);
        counters.BlobPutWrits.ShouldBe(1);
        counters.BlobPutSkips.ShouldBe(0);
        counters.BlobGetCount.ShouldBe(1);
        counters.BlobGetCache.ShouldBe(1);
        counters.BlobGetReads.ShouldBe(0);
    }

    [Theory]
    [InlineData(StoreKind.Testing)]
#if NET8_0_OR_GREATER
    [InlineData(StoreKind.RocksDb)]
#endif
    public async Task Store07PutAgain(StoreKind storeKind)
    {
        string testpath = $"{testroot}{Guid.NewGuid():N}";
        using IDataStore dataStore = TestHelpers.CreateDataStore(storeKind, testpath);

        BlobData data = BlobData.From(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        Memory<byte> idMemory = new byte[BlobIdV1.Size];
        BlobHelpers.CompressData(data.Bytes, idMemory.Span);
        BlobKey key = BlobKey.From(idMemory);

        // put first
        await dataStore.PutBlob(key, data, true);
        var counters1 = dataStore.GetCounters();
        counters1.BlobPutCount.ShouldBe(1);
        counters1.BlobPutWrits.ShouldBe(1);
        counters1.BlobPutSkips.ShouldBe(0);
        counters1.ByteDelta.ShouldBe(256);

        // put again
        await dataStore.PutBlob(key, data, true);

        var counters2 = dataStore.GetCounters();
        counters2.BlobPutCount.ShouldBe(2);
        counters2.BlobPutWrits.ShouldBe(1);
        counters2.BlobPutSkips.ShouldBe(1);
        counters2.ByteDelta.ShouldBe(256);
    }
}
