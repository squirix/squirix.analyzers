using System.Threading;
using System.Threading.Tasks;
using Squirix.Analyzers.UnitTests.Support;

namespace Squirix.Analyzers.UnitTests;

public sealed class GrpcDisposeHttpClientAnalyzerTests
{
    private const string RuleId = "SQR0027";

    private const string Stub = """
                                namespace Grpc.Net.Client
                                {
                                    public sealed class GrpcChannelOptions
                                    {
                                        public System.Net.Http.HttpMessageHandler HttpHandler { get; set; }
                                        public System.Net.Http.HttpClient HttpClient { get; set; }
                                        public bool DisposeHttpClient { get; set; }
                                    }
                                }

                                """;

    [Test]
    public async Task AllowsDisposeFlagAssignedForSameOptions(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   void M()
                                   {
                                       var options = new GrpcChannelOptions();
                                       options.HttpHandler = new SocketsHttpHandler();
                                       options.DisposeHttpClient = true;
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsFlagAssignedAfterInitializer(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   void M(bool own)
                                   {
                                       var options = new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() };
                                       options.DisposeHttpClient = own;
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsDisposeFlagExpressionInInitializer(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   GrpcChannelOptions M(bool own) => new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler(), DisposeHttpClient = own };
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsDisposeFlagInInitializer(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   GrpcChannelOptions M() => new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler(), DisposeHttpClient = true };
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsFieldHandler(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   private readonly HttpMessageHandler _handler = new SocketsHttpHandler();

                                   GrpcChannelOptions M() => new GrpcChannelOptions { HttpHandler = _handler };
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsParameterClient(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   void M(GrpcChannelOptions options, HttpClient client)
                                   {
                                       options.HttpClient = client;
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task AllowsParameterHandler(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   GrpcChannelOptions M(HttpMessageHandler handler) => new GrpcChannelOptions { HttpHandler = handler };
                               }
                               """, cancellationToken);

    [Test]
    public async Task FlagsFactoryCallHandler(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     static HttpMessageHandler CreateHandler() => new SocketsHttpHandler();

                                     GrpcChannelOptions M() => new GrpcChannelOptions { HttpHandler = CreateHandler() };
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsLocalHandlerFromCreation(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     GrpcChannelOptions M()
                                     {
                                         var handler = new SocketsHttpHandler();
                                         return new GrpcChannelOptions { HttpHandler = handler };
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsNewClientInInitializer(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     GrpcChannelOptions M() => new GrpcChannelOptions { HttpClient = new HttpClient() };
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsNewHandlerInInitializer(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     GrpcChannelOptions M() => new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() };
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsPropertyAssignment(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     void M()
                                     {
                                         var options = new GrpcChannelOptions();
                                         options.HttpHandler = new SocketsHttpHandler();
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task AllowsSameSymbolFlagInLambda(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System;
                               using System.Net.Http;
                               using Grpc.Net.Client;

                               class C
                               {
                                   void M()
                                   {
                                       var options = new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() };
                                       Action configure = () => options.DisposeHttpClient = true;
                                   }
                               }
                               """, cancellationToken);

    [Test]
    public async Task FlagsWhenLambdaSetsOtherOptionsFlag(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System;
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     void M()
                                     {
                                         var options = new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() };
                                         Action configure = () =>
                                         {
                                             var options = new GrpcChannelOptions();
                                             options.DisposeHttpClient = true;
                                         };
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsWhenLocalFuncSetsOtherFlag(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     void M()
                                     {
                                         var options = new GrpcChannelOptions();
                                         options.HttpHandler = new SocketsHttpHandler();

                                         void Local()
                                         {
                                             var options = new GrpcChannelOptions();
                                             options.DisposeHttpClient = true;
                                         }
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task FlagsWhenFlagSetOnOtherOptions(CancellationToken cancellationToken) => await AssertFlaggedAsync("""
                                 using System.Net.Http;
                                 using Grpc.Net.Client;

                                 class C
                                 {
                                     void M(GrpcChannelOptions other)
                                     {
                                         var options = new GrpcChannelOptions();
                                         options.HttpClient = new HttpClient();
                                         other.DisposeHttpClient = true;
                                     }
                                 }
                                 """, cancellationToken);

    [Test]
    public async Task IgnoresOtherTypeWithSameMemberNames(CancellationToken cancellationToken) => await AssertCleanAsync("""
                               using System.Net.Http;

                               class Options
                               {
                                   public System.Net.Http.HttpMessageHandler HttpHandler { get; set; }
                               }

                               class C
                               {
                                   Options M() => new Options { HttpHandler = new SocketsHttpHandler() };
                               }
                               """, cancellationToken);

    private static async Task AssertCleanAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new GrpcDisposeHttpClientAnalyzer(), source + Stub, cancellationToken);

        _ = await Assert.That(diagnostics).IsEmpty();
    }

    private static async Task AssertFlaggedAsync(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzerRunner.RunAsync(new GrpcDisposeHttpClientAnalyzer(), source + Stub, cancellationToken);

        var diagnostic = await Assert.That(diagnostics).HasSingleItem();
        _ = await Assert.That(diagnostic.Id).IsEqualTo(RuleId);
    }
}
