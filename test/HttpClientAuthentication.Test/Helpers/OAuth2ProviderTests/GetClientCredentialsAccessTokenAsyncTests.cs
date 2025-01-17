// Copyright © 2025 Rune Gulbrandsen.
// All rights reserved. Licensed under the MIT License; see LICENSE.txt.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using KISS.HttpClientAuthentication.Configuration;
using KISS.HttpClientAuthentication.Constants;
using KISS.HttpClientAuthentication.Helpers;
using KISS.Moq.Logger;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KISS.HttpClientAuthentication.Test.Helpers.OAuth2ProviderTests
{
    public class GetClientCredentialsAccessTokenAsyncTests : TestBase
    {
        [Fact]
        public async Task TestInvalidGrantTypeThrowsArgumentException()
        {
            OAuth2Provider provider = BuildServices().GetRequiredService<OAuth2Provider>();

            Func<Task> act = () => provider.GetClientCredentialsAccessTokenAsync(new(), default!).AsTask();

            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("configuration").WithMessage("GrantType must be ClientCredentials.*");
        }

        [Fact]
        public async Task TestMissingClientCredentialsConfigurationSectionThrowsArgumentException()
        {
            OAuth2Provider provider = BuildServices().GetRequiredService<OAuth2Provider>();

            Func<Task> act = () => provider.GetClientCredentialsAccessTokenAsync(new() { GrantType = OAuth2GrantType.ClientCredentials }, default!)
                                           .AsTask();

            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("configuration").WithMessage("ClientCredentials is null.*");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t\r\n  ")]
        public async Task TestClientIdIsNullEmptyOrWhitespacesThrowsArgumentException(string? clientId)
        {
            IServiceProvider services = BuildServices();

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = clientId!,
                    ClientSecret = "client_secret"
                }
            };

            Func<Task> act = () => provider.GetClientCredentialsAccessTokenAsync(configuration, default!)
                                           .AsTask();

            await act.Should().ThrowAsync<ArgumentException>()
                              .WithParameterName("configuration")
                              .WithMessage("ClientCredentials.ClientId must be specified.*");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t\r\n  ")]
        public async Task TestClientSecretIsNullEmptyOrWhitespacesThrowsArgumentException(string? clientSecret)
        {
            IServiceProvider services = BuildServices();

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = clientSecret!
                }
            };

            Func<Task> act = () => provider.GetClientCredentialsAccessTokenAsync(configuration, default!)
                                           .AsTask();

            await act.Should().ThrowAsync<ArgumentException>()
                              .WithParameterName("configuration")
                              .WithMessage("ClientCredentials.ClientSecret must be specified.*");
        }

        [Fact]
        public async Task TestMissingTokenEndpointConfigurationSectionThrowsArgumentException()
        {
            OAuth2Provider provider = BuildServices().GetRequiredService<OAuth2Provider>();

            Func<Task> act = () => provider.GetClientCredentialsAccessTokenAsync(new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                }
            }, default!).AsTask();

            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("configuration").WithMessage("TokenEndpoint is null.*");
        }

        [Fact]
        public async Task TestMissingTokenEndpointUrlThrowsArgumentException()
        {
            OAuth2Provider provider = BuildServices().GetRequiredService<OAuth2Provider>();

            Func<Task> act = () => provider.GetClientCredentialsAccessTokenAsync(new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                },
                TokenEndpoint = new()
            }, default!).AsTask();

            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("configuration").WithMessage("TokenEndpoint.Url must be specified.*");
        }

        [Fact]
        public async Task TestCacheHitIsReturnedAsToken()
        {
            IServiceProvider services = BuildServices();

            Mock<IMemoryCache> memoryCacheMock = services.GetRequiredService<Mock<IMemoryCache>>();

            object expected = new AccessTokenResponse
            {
                AccessToken = "Access_Token",
                TokenType = "Token_Type"
            };

            memoryCacheMock.Setup(memoryCache => memoryCache.TryGetValue("ClientCredentials#https://somehost/#client_id", out expected!))
                           .Returns(true);

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                }
            };

            AccessTokenResponse? result = await provider.GetClientCredentialsAccessTokenAsync(configuration, default);

            result.Should().NotBeNull();
            result!.AccessToken.Should().Be("Access_Token");
            result!.TokenType.Should().Be("Token_Type");
            result!.ExpiresIn.Should().BeNull();


            Mock<ILogger<OAuth2Provider>> loggerMock = services.GetRequiredService<Mock<ILogger<OAuth2Provider>>>();

            loggerMock.VerifyExt(l => l.LogDebug("Token for {TokenEndpoint} with client id {ClientId} found in cache, using this.",
                                                       "https://somehost/", "client_id"), Times.Once);
        }

        [Fact]
        public async Task TestGetAndCacheAccessTokenResponse()
        {
            IServiceProvider services = BuildServices();

            AccessTokenResponse expected = new()
            {
                AccessToken = "ACCESS_TOKEN",
                TokenType = "TOKEN_TYPE",
                ExpiresIn = 3600
            };

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                          {
                              Content = JsonContent.Create(expected)
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                }
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            AccessTokenResponse? result = await provider.GetClientCredentialsAccessTokenAsync(configuration, default);

            result.Should().NotBeNull();

            result.Should().BeEquivalentTo(expected);

            Mock<IMemoryCache> memoryCacheMock = services.GetRequiredService<Mock<IMemoryCache>>();
            memoryCacheMock.Verify(memoryCache => memoryCache.CreateEntry("ClientCredentials#https://somehost/#client_id"), Times.Once);

            Mock<ICacheEntry> cacheEntryMock = services.GetRequiredService<Mock<ICacheEntry>>();

            cacheEntryMock.VerifySet(cacheEntry => cacheEntry.Value = result, Times.Once);
            cacheEntryMock.VerifySet(cacheEntry => cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(3420), Times.Once);

            Mock<ILogger<OAuth2Provider>> loggerMock = services.GetRequiredService<Mock<ILogger<OAuth2Provider>>>();

            loggerMock.VerifyExt(l => l.LogDebug("Could not find existing token in cache, requesting token from endpoint {TokenEndpoint} with client id {ClientId}.",
                                                 "https://somehost/", "client_id"), Times.Once);

            loggerMock.VerifyExt(l => l.LogDebug("Token retrieved from {TokenEndpoint} with client id {ClientId} and cached for {CacheExpiresIn} seconds.",
                                                       "https://somehost/", "client_id", 3420), Times.Once);
        }

        [Fact]
        public async Task TestNoCachingOfAccessTokenResponseWithMissingExpiresIn()
        {
            IServiceProvider services = BuildServices();

            AccessTokenResponse expected = new()
            {
                AccessToken = "ACCESS_TOKEN",
                TokenType = "TOKEN_TYPE",
                ExpiresIn = null
            };

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                          {
                              Content = JsonContent.Create(expected)
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                }
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            await provider.GetClientCredentialsAccessTokenAsync(configuration, default);

            Mock<IMemoryCache> memoryCacheMock = services.GetRequiredService<Mock<IMemoryCache>>();
            memoryCacheMock.Verify(memoryCache => memoryCache.CreateEntry("ClientCredentials#https://somehost/#client_id"), Times.Never);

            Mock<ILogger<OAuth2Provider>> loggerMock = services.GetRequiredService<Mock<ILogger<OAuth2Provider>>>();

            loggerMock.VerifyExt(l => l.LogDebug("Token retrieved from {TokenEndpoint} with client id {ClientId}, but not cached since it is missing expires_in information.",
                                                       "https://somehost/", "client_id"), Times.Once);
        }

        [Fact]
        public async Task TestNoCachingOfAccessTokenResponseWhenCacheIsDiabled()
        {
            IServiceProvider services = BuildServices();

            AccessTokenResponse expected = new()
            {
                AccessToken = "ACCESS_TOKEN",
                TokenType = "TOKEN_TYPE",
                ExpiresIn = null
            };

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                          {
                              Content = JsonContent.Create(expected)
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                },
                DisableTokenCache = true
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            await provider.GetClientCredentialsAccessTokenAsync(configuration, default);

            Mock<IMemoryCache> memoryCacheMock = services.GetRequiredService<Mock<IMemoryCache>>();
            memoryCacheMock.Verify(memoryCache => memoryCache.CreateEntry("ClientCredentials#https://somehost/#client_id"), Times.Never);

            Mock<ILogger<OAuth2Provider>> loggerMock = services.GetRequiredService<Mock<ILogger<OAuth2Provider>>>();

            loggerMock.VerifyExt(l => l.LogDebug("Token retrieved from {TokenEndpoint} with client id {ClientId}, but the token cache is disabled.",
                                                       "https://somehost/", "client_id"), Times.Once);
        }

        [Fact]
        public async Task TestUseFormBasedAuthentication()
        {
            IServiceProvider services = BuildServices();

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            HttpRequestMessage? actualRequest = null;

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .Callback((HttpRequestMessage request, CancellationToken _) => actualRequest = request)
                          .ReturnsAsync(() =>
                          {
                              actualRequest!.Headers.Authorization.Should().BeNull();

                              string content = actualRequest.Content!.ReadAsStringAsync(default).GetAwaiter().GetResult();
                              content.Should().Be($"grant_type=client_credentials&client_id=client_id&client_secret=client_secret");

                              return new HttpResponseMessage(HttpStatusCode.NotFound);
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                }
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            await provider.GetClientCredentialsAccessTokenAsync(configuration, default);
        }

        [Fact]
        public async Task TestUseBasicAuthentication()
        {
            IServiceProvider services = BuildServices();

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            HttpRequestMessage? actualRequest = null;

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .Callback((HttpRequestMessage request, CancellationToken _) => actualRequest = request)
                          .ReturnsAsync(() =>
                          {
                              actualRequest!.Headers.Authorization.Should().NotBeNull();

                              actualRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
                              actualRequest.Headers.Authorization!.Parameter.Should().Be(Convert.ToBase64String(Encoding.ASCII.GetBytes($"client_id:client_secret")));

                              string content = actualRequest.Content!.ReadAsStringAsync(default).GetAwaiter().GetResult();
                              content.Should().Be($"grant_type=client_credentials");
                              return new HttpResponseMessage(HttpStatusCode.NotFound);
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    UseBasicAuthorizationHeader = true,
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                }
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            await provider.GetClientCredentialsAccessTokenAsync(configuration, default);
        }

        [Fact]
        public async Task TestRequestContainsScopeWhenSpecified()
        {
            IServiceProvider services = BuildServices();

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            HttpRequestMessage? actualRequest = null;

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .Callback((HttpRequestMessage request, CancellationToken _) => actualRequest = request)
                          .ReturnsAsync(() =>
                          {
                              actualRequest!.Headers.Authorization.Should().BeNull();

                              string content = actualRequest.Content!.ReadAsStringAsync(default).GetAwaiter().GetResult();
                              content.Should().Contain($"scope=test_scope");

                              return new HttpResponseMessage(HttpStatusCode.NotFound);
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret",
                },
                Scope = "test_scope"
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            await provider.GetClientCredentialsAccessTokenAsync(configuration, default);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(" \t\r\n")]
        public async Task TestRequestHasNoScopeWhenNullEmptyOrWhitespace(string? scope)
        {
            IServiceProvider services = BuildServices();

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            HttpRequestMessage? actualRequest = null;

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .Callback((HttpRequestMessage request, CancellationToken _) => actualRequest = request)
                          .ReturnsAsync(() =>
                          {
                              actualRequest!.Headers.Authorization.Should().BeNull();

                              string content = actualRequest.Content!.ReadAsStringAsync(default).GetAwaiter().GetResult();
                              content.Should().NotContain($"scope=");

                              return new HttpResponseMessage(HttpStatusCode.NotFound);
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new() { Url = new("https://somehost/") },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret",
                },
                Scope = scope
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            await provider.GetClientCredentialsAccessTokenAsync(configuration, default);
        }


        [Fact]
        public async Task TestRequestContainsAdditionalParametersSpecified()
        {
            IServiceProvider services = BuildServices();

            Mock<HttpClient> httpClientMock = services.GetRequiredService<Mock<HttpClient>>();

            HttpRequestMessage? actualRequest = null;

            httpClientMock.Setup(httpClient => httpClient.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                          .Callback((HttpRequestMessage request, CancellationToken _) => actualRequest = request)
                          .ReturnsAsync(() =>
                          {
                              actualRequest!.RequestUri!.Query.Should().Be("?query=query_value_with_%3F");

                              actualRequest.Headers.Should().Contain(kvp =>
                                kvp.Key == "header" && kvp.Value.All(v => "header_value".Equals(v)));

                              string content = actualRequest.Content!.ReadAsStringAsync(default).GetAwaiter().GetResult();

                              content.Should().Contain("body=body_value");

                              return new HttpResponseMessage(HttpStatusCode.NotFound);
                          });

            OAuth2Configuration configuration = new()
            {
                GrantType = OAuth2GrantType.ClientCredentials,
                TokenEndpoint = new()
                {
                    Url = new("https://somehost/"),
                    AdditionalBodyParameters =
                    {
                        { "body", "body_value" }
                    },
                    AdditionalHeaderParameters =
                    {
                        { "header", "header_value" }
                    },
                    AdditionalQueryParameters =
                    {
                        { "query", "query_value_with_?" }
                    }
                },
                ClientCredentials = new()
                {
                    ClientId = "client_id",
                    ClientSecret = "client_secret"
                }
            };

            OAuth2Provider provider = services.GetRequiredService<OAuth2Provider>();

            await provider.GetClientCredentialsAccessTokenAsync(configuration, default);
        }
    }
}
