﻿using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions.Internal;
using Newtonsoft.Json;
using Polly;
using HttpApiClient.Extensions;
using HttpApiClient.Models;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Converters;

namespace HttpApiClient
{
    // ApiClient must be subclassed as a specific type to use it
    public abstract class ApiClient<TClient> where TClient : class
    {
        protected HttpClient _client;
        protected readonly ILogger<TClient> _logger;
        private ApiResponseBuilder<TClient> _apiResponseBuilder;

        protected ApiClientOptions<TClient> _options;
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public int PendingRequestCount { get; set; }
        public int RequestCount { get; set; }
        public DateTimeOffset LastRequestTimeStamp { get; private set; }

        public ApiClient(HttpClient client, ApiClientOptions<TClient> options, ILogger<TClient> logger)
        {
            _client = client;
            _options = options;
            _logger = logger;

            // Get class name for demo purposes, the class name is shown in the log output anyway
            var name = TypeNameHelper.GetTypeDisplayName(typeof(TClient));
            _logger.LogDebug($"{name} constructed");
            _logger.LogDebug($"{name} BaseAddress: {client.BaseAddress}");

            // Create the ApiResponseBuilder
            _apiResponseBuilder = new ApiResponseBuilder<TClient>(_logger);  // new is glue is fine here
            _apiResponseBuilder.KnownErrorParsers = options.KnownErrorParsers;

            // Create a Cancellation token
            CancellationTokenSource = new CancellationTokenSource();
        }

        public void SetBasicAuth(string username, string password)
        {
            string basicAuth = string.Format("{0}:{1}", username, password);
            string encodedAuth = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(basicAuth));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encodedAuth);
        }

        public void clearBasicAuth(string bearerToken)
        {
            _client.DefaultRequestHeaders.Remove("Authorization");
        }

        // Set the OAuth 2.0 bearer token
        public void SetBearerToken(string bearerToken)
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        public void clearBearerToken(string bearerToken)
        {
            _client.DefaultRequestHeaders.Remove("Authorization");
        }

        // Set the Cookie
        public void SetCookie(string cookie)
        {
            _client.DefaultRequestHeaders.Add("Cookie", cookie);
        }

        public void clearCookie(string bearerToken)
        {
            _client.DefaultRequestHeaders.Remove("Cookie");
        }

        public virtual async Task<ApiResponse> GetResource(string resourcePath, TimeSpan? delay = null) {
            if (delay.HasValue) {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Getting Resource {resourcePath} after delay {delay.Value}...");
                await Task.Delay(delay.Value);
            } else {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Getting Resource {resourcePath} ...");
            }
            return await SendAsync(HttpMethod.Get, resourcePath);
        }

        public virtual async Task<ApiResponse> PostResource(string resourcePath, object obj, TimeSpan? delay = null) {
            if (delay.HasValue) {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Posting Resource {resourcePath} after delay {delay.Value}...");
                await Task.Delay(delay.Value);
            } else {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Posting Resource {resourcePath} ...");
            }
            return await SendObjectAsync(HttpMethod.Post, resourcePath, obj);                   
        }

        public virtual async Task<ApiResponse> PutResource(string resourcePath, object obj, TimeSpan? delay = null) {
            if (delay.HasValue) {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Putting Resource {resourcePath} after delay {delay.Value}...");
                await Task.Delay(delay.Value);
            } else {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Putting Resource {resourcePath} ...");
            }
            return await SendObjectAsync(HttpMethod.Put, resourcePath, obj);                   
        }

        public virtual async Task<ApiResponse> DeleteResource(string resourcePath, TimeSpan? delay = null) {
            if (delay.HasValue) {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Deleting Resource {resourcePath} after delay {delay.Value}...");
                await Task.Delay(delay.Value);
            } else {
                _logger.LogDebug($"{DateTime.Now.ToString()} : Deleting Resource {resourcePath} ...");
            }
            return await SendAsync(HttpMethod.Delete, resourcePath);                   
        }

        // Send a string with optional media type
        public virtual async Task<ApiResponse> SendStringAsync(HttpMethod method, string resourcePath, string str, string mediaType = "text/plain") {
            _logger.LogDebug("{DateTime.Now.ToString()} : SendAsync: Sending string as StringContent");
            StringContent stringContent = null; // StringContent: Provides HTTP content based on a string.
            if (str != null) {
                stringContent = new StringContent(str, System.Text.Encoding.UTF8, mediaType);
            }
            return await SendAsync(method, resourcePath, stringContent);
        }

        // Send an object as JSON
        public virtual async Task<ApiResponse> SendObjectAsync(HttpMethod method, string resourcePath, object obj) {
            StringContent stringContent = null;
            if (obj != null) {
                string str = obj as string;
                if (str != null) {
                    _logger.LogDebug("{DateTime.Now.ToString()} : SendObjectAsync: Sending string as StringContent");
                    stringContent = new StringContent(str, System.Text.Encoding.UTF8, "text/plain");
                } else {
                    _logger.LogDebug($"{DateTime.Now.ToString()} : SendObjectAsync: Sending object as JSON Serialized StringContent");
                    var settings = new JsonSerializerSettings {
                        ContractResolver = new DefaultContractResolver(),
                        ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore, // Avoids cyclic references
                        NullValueHandling = NullValueHandling.Ignore
                    };
                    if (_options.SerializeNullValues) {
                        settings.NullValueHandling = NullValueHandling.Include;
                    }
                    if (_options.SerializeEnumsAsStrings) {
                        settings.Converters = new JsonConverter[] { new StringEnumConverter() };
                    }
                    if (_options.SerializePropertiesAsCamelCase) {
                        settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    }
                    try {
                        _logger.LogDebug($"{DateTime.Now.ToString()} : SendObjectAsync: Serializing Object of Type: {obj?.GetType()?.Name?.ToString()} to JSON");
                        string jsonString = JsonConvert.SerializeObject(obj, _options.SerializeUseFormattingIndented ? Formatting.Indented : Formatting.None, settings);
                        stringContent = new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json");
                    } catch (Exception exception) {
                        //log exception but don't throw one
                        _logger.LogError($"{DateTime.Now.ToString()} : SendObjectAsync: Exception occurred during Serialization while attempting to {method?.ToString()} resource: {resourcePath}\nException: {exception.ToString()}");
                        return null;
                    }
                }
            }
            return await SendAsync(method, resourcePath, stringContent);                    
        }

        // Send HttpContent, used by all above methods, accept any of the HttpContent sub-classes
        // FormUrlEncodedContent: A container for name/value tuples encoded using application/x-www-form-urlencoded MIME type.
        // MultipartContent: Provides a collection of HttpContent objects that get serialized using the multipart/* content type specification.
        // MultipartFormDataContent: Provides a container for content encoded using multipart/form-data MIME type.
        // StreamContent: Provides HTTP content based on a stream.
        // StringContent: Provides HTTP content based on a string.
        // ObjectContent: Contains a value as well as an associated MediaTypeFormatter that will be used to serialize the value when writing this content.
        // ObjectContent comes from the System.Net.Http.Formatting assembly provided by package Microsoft.AspNet.WebApi.Client
        // HttpContent: A base class representing an HTTP entity body and content headers.
        public virtual async Task<ApiResponse> SendAsync(HttpMethod method, string resourcePath, HttpContent content = null) {
            // Create the context here so we have access to it in the catch block
            Polly.Context context = new Polly.Context();
            //Create the Request
            HttpRequestMessage request = new HttpRequestMessage(method, resourcePath);
            if (content != null) {
                request.Content = content;
            } else {
                // content is normally provided for post and put methods
                if (method == HttpMethod.Post || method == HttpMethod.Put) _logger.LogDebug($"{DateTime.Now.ToString()} : SendAsync: The HttpContent is null for POST or PUT request!");
            }
            // Set the PolicyExecutionContext so that it is available after execution of the request
            // https://github.com/App-vNext/Polly/issues/505
            request.SetPolicyExecutionContext(context);
            request.SetResourcePath(resourcePath);
            // Make the request
            RequestCount++;
            PendingRequestCount++;
            LastRequestTimeStamp = DateTime.UtcNow;
            try {
                _logger.LogDebug($"{DateTime.Now.ToString()} : SendAsync: Sending request with Method: {method?.ToString()} HttpContent Type: {content?.GetType()?.Name?.ToString()} to Resource: {resourcePath}");
                using(var response = await _client.SendAsync(request, CancellationTokenSource.Token)) {
                    TransferRetryInfo(response.RequestMessage, context);
                    return await _apiResponseBuilder.GetApiResponse(response, resourcePath);
                } 
            } catch (Exception exception) {
                // Handles communication errors such as "Connection Refused" etc.
                // Network failures (System.Net.Http.HttpRequestException)
                // Timeouts (System.IO.IOException)
                TransferRetryInfo(exception, context);
                return _apiResponseBuilder.GetApiResponse(exception, request, resourcePath);
            } finally {
                PendingRequestCount--;
            }                    
        }

        // Transfers the RetryInfo from the PolicyExecutionContext into the Request Properties
        private void TransferRetryInfo(HttpRequestMessage request, Polly.Context context) {
            RetryInfo retryInfo = context.GetRetryInfo();
            if (retryInfo != null) {
                request.SetRetryInfo(retryInfo);
            }
        }

        // Transfers the RetryInfo from the PolicyExecutionContext into the Exception Data
        private void TransferRetryInfo(Exception exception, Polly.Context context) {
            RetryInfo retryInfo = context.GetRetryInfo();
            if (retryInfo != null) {
                exception.SetRetryInfo(retryInfo);
            }
        }
    }
}