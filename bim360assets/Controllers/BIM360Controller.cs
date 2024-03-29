/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Developer Advocate and Support
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using System;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Autodesk.Forge;
using RestSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using bim360assets.Models;
using System.Web;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace bim360assets.Controllers
{
    public partial class BIM360Controller : ControllerBase
    {
        private const string BASE_URL = "https://developer.api.autodesk.com";

        [HttpGet]
        [Route("api/aps/bim360/container")]
        public async Task<dynamic> GetContainerAsync(string href)
        {
            string[] idParams = href.Split('/');
            string projectId = idParams[idParams.Length - 1];
            string hubId = idParams[idParams.Length - 3];

            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);

            ProjectsApi projectsApi = new ProjectsApi();
            projectsApi.Configuration.AccessToken = credentials.TokenInternal;
            var project = await projectsApi.GetProjectAsync(hubId, projectId);
            var issues = project.data.relationships.issues.data;
            if (issues.type != "issueContainerId") return null;
            return new { ContainerId = issues["id"], HubId = hubId };
        }

        private async Task<RestResponse> GetUsers(string accountId)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            dynamic bearer = await oauth.AuthenticateAsync(Credentials.GetAppSetting("APS_CLIENT_ID"), Credentials.GetAppSetting("APS_CLIENT_SECRET"), "client_credentials", new Scope[] { Scope.AccountRead });

            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/hq/v1/accounts/{account_id}/users", RestSharp.Method.Get);
            request.AddParameter("account_id", accountId.Replace("b.", string.Empty), ParameterType.UrlSegment);

            string token = bearer.access_token;
            request.AddHeader("Authorization", "Bearer " + token);

            return await client.ExecuteAsync(request);
        }

        [HttpGet]
        [Route("api/aps/bim360/hq/project")]
        public IActionResult GetHqProjectAsync(string href)
        {
            string[] idParams = href.Split('/');
            string projectId = idParams[idParams.Length - 1];
            string hubId = idParams[idParams.Length - 3];

            return Ok(new { ProjectId = projectId.Replace("b.", string.Empty), HubId = hubId.Replace("b.", string.Empty) });
        }

        [HttpGet]
        [Route("api/aps/bim360/account/{accountId}/project/{projectId}/users")]
        public async Task<IActionResult> GetBIM360ProjectUsersAsync(string accountId, [FromQuery] Nullable<int> pageOffset = null, [FromQuery] Nullable<int> pageLimit = null)
        {
            RestResponse usersResponse = await GetUsers(accountId/*, pageOffset, pageLimit*/);
            var users = JsonConvert.DeserializeObject<List<User>>(usersResponse.Content);
            return Ok(users);
        }

        private async Task<RestResponse> GetProjectUsers(string accountId, string projectId, Nullable<int> pageOffset = null, Nullable<int> pageLimit = null)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            dynamic bearer = await oauth.AuthenticateAsync(Credentials.GetAppSetting("APS_CLIENT_ID"), Credentials.GetAppSetting("APS_CLIENT_SECRET"), "client_credentials", new Scope[] { Scope.AccountRead });

            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/bim360/admin/v1/projects/{project_id}/users", RestSharp.Method.Get);
            request.AddParameter("project_id", projectId.Replace("b.", string.Empty), ParameterType.UrlSegment);

            string token = bearer.access_token;
            request.AddHeader("Authorization", "Bearer " + token);

            return await client.ExecuteAsync(request);
        }

        [HttpGet]
        [Route("api/aps/bim360/account/{accountId}/project/{projectId}/assets")]
        public async Task<IActionResult> GetBIM360AssetsAsync(string accountId, string projectId, [FromQuery] string cursorState, [FromQuery] Nullable<int> pageLimit = null)
        {
            RestResponse assetsResponse = await GetAssetsAsync(projectId, cursorState, pageLimit);
            var assets = JsonConvert.DeserializeObject<PaginatedAssets>(assetsResponse.Content);

            string nextUrl = null;

            if (!string.IsNullOrWhiteSpace(assets.Pagination.NextUrl))
            {
                var nextUri = new Uri(assets.Pagination.NextUrl);
                string nextCursorState = HttpUtility.ParseQueryString(nextUri.Query).Get("cursorState");

                var queries = new Dictionary<string, string>
                {
                    { "cursorState", nextCursorState }
                };

                if (pageLimit.HasValue)
                {
                    queries.Add("pageLimit", pageLimit.Value.ToString());
                }

                nextUrl = UriHelper.BuildAbsolute(
                    HttpContext.Request.Scheme,
                    HttpContext.Request.Host,
                    HttpContext.Request.Path
                );
                nextUrl = QueryHelpers.AddQueryString(nextUrl, queries);
            }

            //!-- Workaround: Navigating to previous page
            string previousUrl = null;
            if (assets.Pagination.Offset > 0)
            {
                var offest = assets.Pagination.Offset - assets.Pagination.Limit;
                var prevCursorState = new
                {
                    offset = offest <= 0 ? 0 : offest,
                    limit = assets.Pagination.Limit
                };
                var prevCursorStateStr = JsonConvert.SerializeObject(prevCursorState);
                var encodedPrevCursorStateStr = Convert.ToBase64String(Encoding.UTF8.GetBytes(prevCursorStateStr));

                var queries = new Dictionary<string, string>
                {
                    { "cursorState", encodedPrevCursorStateStr }
                };

                if (pageLimit.HasValue)
                {
                    queries.Add("pageLimit", pageLimit.Value.ToString());
                }

                previousUrl = UriHelper.BuildAbsolute(
                    HttpContext.Request.Scheme,
                    HttpContext.Request.Host,
                    HttpContext.Request.Path
                );
                previousUrl = QueryHelpers.AddQueryString(previousUrl, queries);
            }

            return Ok(new
            {
                Pagination = new Pagination
                {
                    Limit = assets.Pagination.Limit,
                    Offset = assets.Pagination.Offset,
                    CursorState = assets.Pagination.CursorState,
                    PreviousUrl = previousUrl,
                    NextUrl = nextUrl,
                },
                Results = assets.Results
            });
        }

        private async Task<RestResponse> GetAssetsAsync(string projectId, string cursorState, Nullable<int> pageLimit = null)
        {
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (credentials == null)
            {
                throw new InvalidOperationException("Failed to refresh access token");
            }

            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/bim360/assets/v2/projects/{project_id}/assets", RestSharp.Method.Get);
            request.AddParameter("project_id", projectId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddParameter("includeCustomAttributes", true, ParameterType.QueryString);
            request.AddParameter("sort", "categoryId asc,clientAssetId asc", ParameterType.QueryString);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);

            if (!string.IsNullOrWhiteSpace(cursorState))
            {
                request.AddParameter("cursorState", cursorState, ParameterType.QueryString);
            }

            if (pageLimit != null && pageLimit.HasValue)
            {
                request.AddParameter("limit", pageLimit.Value, ParameterType.QueryString);
            }

            return await client.ExecuteAsync(request);
        }

        private async Task<bool> GetAllAssetsAsync(string projectId, List<Asset> assets, string cursorState, Nullable<int> pageLimit = null)
        {
            projectId = projectId.Replace("b.", string.Empty);
            RestResponse assetsResponse = await GetAssetsAsync(projectId, cursorState, pageLimit);
            var data = JsonConvert.DeserializeObject<PaginatedAssets>(assetsResponse.Content);

            if (data.Results == null || data.Results.Count <= 0)
                return false;

            assets.AddRange(data.Results);

            if (data.Pagination.CursorState != null)
            {
                var nextCursorState = new
                {
                    offset = data.Pagination.Offset + data.Pagination.Limit,
                    limit = data.Pagination.Limit
                };
                var nextCursorStateStr = JsonConvert.SerializeObject(nextCursorState);
                var encodedNextCursorStateStr = Convert.ToBase64String(Encoding.UTF8.GetBytes(nextCursorStateStr));

                await GetAllAssetsAsync(projectId, assets, encodedNextCursorStateStr, data.Pagination.Limit);
            }

            return true;
        }

        [HttpGet]
        [Route("api/aps/bim360/account/{accountId}/project/{projectId}/assets/{assetId}")]
        public async Task<IActionResult> GetBIM360AssetByIdAsync(string accountId, string projectId, string assetId, [FromQuery] bool flatten = false)
        {
            //var asset = await GetAssetsByExtIdAsync(projectId, assetId);
            var asset = await GetAssetsBySearchTextAsync(projectId, assetId);
            if (asset == null)
                return NotFound($"No asset with id: {assetId}");

            if (!flatten)
            {
                return Ok(asset);
            }
            else
            {
                RestResponse usersResponse = await GetUsers(accountId);
                var users = JsonConvert.DeserializeObject<List<User>>(usersResponse.Content);
                var userMapping = users.ToDictionary(u => u.Uid, u => u);
                Func<string, string> getUserName = (uid) => (!string.IsNullOrWhiteSpace(uid) && userMapping.ContainsKey(uid)) ? userMapping[uid].Name : string.Empty;
                asset.CreatedByUser = getUserName(asset.CreatedBy);
                asset.UpdatedByUser = getUserName(asset.UpdatedBy);
                asset.DeletedByUser = getUserName(asset.DeletedBy);
                asset.InstalledByUser = getUserName(asset.InstalledBy);

                var properties = await this.FlatProperties(asset, projectId);

                return Ok(new
                {
                    id = asset.Id,
                    externalId = asset.ClientAssetId,
                    properties
                });
            }
        }

        private int ConvertAttributeType(Type type)
        {
            int viewerAttributeType = 0;

            if (type == typeof(bool))
            {
                viewerAttributeType = 1;
            }
            else if (type == typeof(int))
            {
                viewerAttributeType = 2;
            }
            else if (type == typeof(double) || type == typeof(float))
            {
                viewerAttributeType = 3;
            }
            else if (type == typeof(string))
            {
                viewerAttributeType = 20;
            }
            else if (type == typeof(DateTime))
            {
                viewerAttributeType = 22;
            }
            else
            {
                viewerAttributeType = 0;
            }

            return viewerAttributeType;
        }

        private async Task<List<ViewerProperty>> FlatProperties(Dictionary<string, object> customAttributes, string projectId)
        {
            var properties = new List<ViewerProperty>();
            var attrDefsResponse = await GetCustomAttributeDefsAsync(projectId, null, 100);
            var attrDefs = JsonConvert.DeserializeObject<PaginatedAssetCustomAttributes>(attrDefsResponse.Content);
            var attrDefMapping = attrDefs.Results.ToDictionary(d => d.Name, d => d);
            Func<string, string> getAttrDefName = (name) => (!string.IsNullOrWhiteSpace(name) && attrDefMapping.ContainsKey(name)) ? attrDefMapping[name].DisplayName : string.Empty;

            foreach (var pair in customAttributes)
            {
                var attrName = pair.Key;
                var attrVal = pair.Value.ToString();
                var attrType = this.ConvertAttributeType(attrVal.GetType());

                //!-- Todo: get custom attribute name from Assets API
                var property = new ViewerProperty
                {
                    AttributeName = attrName,
                    DisplayCategory = "Custom",
                    DisplayName = getAttrDefName(attrName),
                    DisplayValue = attrVal,
                    Hidden = 0,
                    Precision = 0,
                    Type = attrType,
                    Units = null
                };

                properties.Add(property);
            }
            return properties;
        }

        private async Task<List<ViewerProperty>> FlatProperties(Asset asset, string projectId)
        {
            var properties = new List<ViewerProperty>();
            foreach (System.Reflection.PropertyInfo pi in asset.GetType().GetProperties())
            {
                var attrName = pi.Name;
                if (attrName == "CustomAttributes")
                {
                    var flattenCustomAttrs = await this.FlatProperties(((Dictionary<string, object>)pi.GetValue(asset, null)), projectId);
                    properties.AddRange(flattenCustomAttrs);
                    continue;
                }

                var isHidden = (attrName.EndsWith("Id") || attrName.EndsWith("By")) ? 1 : 0;
                var attrType = this.ConvertAttributeType(pi.PropertyType);
                var attrVal = pi.GetValue(asset, null)?.ToString();

                if (pi.PropertyType == typeof(DateTime?))
                    attrVal = ((DateTime?)pi.GetValue(asset, null))?.ToUniversalTime()
                                            .ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'");

                var property = new ViewerProperty
                {
                    AttributeName = attrName,
                    DisplayCategory = "Built In",
                    DisplayName = Regex.Replace(attrName, "(\\B[A-Z])", " $1"),
                    DisplayValue = attrVal,
                    Hidden = isHidden,
                    Precision = 0,
                    Type = attrType,
                    Units = null
                };
                properties.Add(property);
            }
            return properties;
        }

        private async Task<Asset> GetAssetsBySearchTextAsync(string projectId, string text)
        {
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (credentials == null)
            {
                throw new InvalidOperationException("Failed to refresh access token");
            }

            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/bim360/assets/v2/projects/{project_id}/assets", RestSharp.Method.Get);
            request.AddParameter("project_id", projectId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddParameter("includeCustomAttributes", true, ParameterType.QueryString);
            request.AddParameter("filter[searchText]", text, ParameterType.QueryString);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);

            RestResponse assetsResponse = await client.ExecuteAsync(request);
            var assets = JsonConvert.DeserializeObject<PaginatedAssets>(assetsResponse.Content);

            if (assets.Results == null || assets.Results.Count <= 0)
                return null;

            return assets.Results.FirstOrDefault();
        }

        private async Task<Asset> GetAssetsByExtIdAsync(string projectId, string id)
        {
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (credentials == null)
            {
                throw new InvalidOperationException("Failed to refresh access token");
            }

            var attrDefsResponse = await GetCustomAttributeDefsAsync(projectId.Replace("b.", string.Empty), null, 100);
            var attrDefs = JsonConvert.DeserializeObject<PaginatedAssetCustomAttributes>(attrDefsResponse.Content);
            var attrDefMapping = attrDefs.Results.ToDictionary(d => d.DisplayName, d => d);
            var extIdAttr = attrDefs.Results.First(attr => attr.DisplayName.ToLower().Contains("External Id".ToLower()));

            if (extIdAttr == null)
            {
                throw new InvalidOperationException("Failed to get CustomAttribute called `External Id`");
            }

            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/bim360/assets/v2/projects/{project_id}/assets", RestSharp.Method.Get);
            request.AddParameter("project_id", projectId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddParameter("includeCustomAttributes", true, ParameterType.QueryString);

            var attrFilter = string.Format("filter[customAttributes][{0}]", extIdAttr.Name);
            request.AddParameter(attrFilter, id, ParameterType.QueryString);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);

            RestResponse assetsResponse = await client.ExecuteAsync(request);
            var assets = JsonConvert.DeserializeObject<PaginatedAssets>(assetsResponse.Content);

            if (assets.Results == null || assets.Results.Count <= 0)
                return null;

            return assets.Results.FirstOrDefault();
        }

        private async Task<Asset> GetAssetsByIdAsync(string projectId, string id, string cursorState, Nullable<int> pageLimit = null)
        {
            RestResponse assetsResponse = await GetAssetsAsync(projectId.Replace("b.", string.Empty), cursorState, pageLimit);
            var assets = JsonConvert.DeserializeObject<PaginatedAssets>(assetsResponse.Content);

            if (assets.Results == null || assets.Results.Count <= 0)
                return null;

            Asset asset = assets.Results
                                .Where(a =>
                                    a.Id == id ||
                                    a.ClientAssetId == id ||
                                    (a.CustomAttributes != null && a.CustomAttributes.Values.Any(p => p.ToString().Contains(id)))
                                )
                                .FirstOrDefault();

            if (asset == null && assets.Pagination.CursorState != null)
            {
                var nextCursorState = new
                {
                    offset = assets.Pagination.Offset + assets.Pagination.Limit,
                    limit = assets.Pagination.Limit
                };
                var nextCursorStateStr = JsonConvert.SerializeObject(nextCursorState);
                var encodedNextCursorStateStr = Convert.ToBase64String(Encoding.UTF8.GetBytes(nextCursorStateStr));

                return await GetAssetsByIdAsync(projectId, id, encodedNextCursorStateStr, assets.Pagination.Limit);
            }

            #region DEBUG
            // Asset asset = null;
            // var found = false;
            // foreach (var a in assets.Results)
            // {
            //     if(found)
            //         break;

            //     if (a.Id == id || a.ClientAssetId == id)
            //     {
            //         asset = a;
            //         found = true;
            //         break;
            //     }
            //     else
            //     {
            //         if (a.CustomAttributes != null)
            //         {
            //             foreach (var p in a.CustomAttributes.Values)
            //             {
            //                 if (p.ToString().Contains(id))
            //                 {
            //                     asset = a;
            //                     found = true;
            //                     break;
            //                 }
            //             }
            //         }
            //     }
            // }
            #endregion

            return asset;
        }

        [HttpGet]
        [Route("api/aps/bim360/account/{accountId}/project/{projectId}/asset-custom-attr-defs")]
        public async Task<IActionResult> GetBIM360CustomAttributeDefsAsync(string accountId, string projectId, [FromQuery] string cursorState, [FromQuery] Nullable<int> pageLimit = null)
        {
            var attrDefsResponse = await GetCustomAttributeDefsAsync(projectId.Replace("b.", string.Empty), cursorState, pageLimit);
            var attrDefs = JsonConvert.DeserializeObject<PaginatedAssetCustomAttributes>(attrDefsResponse.Content);

            return Ok(attrDefs.Results);
        }

        private async Task<RestResponse> GetCustomAttributeDefsAsync(string projectId, string cursorState, Nullable<int> pageLimit = null)
        {
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/bim360/assets/v1/projects/{project_id}/custom-attributes", RestSharp.Method.Get);
            request.AddParameter("project_id", projectId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);

            if (!string.IsNullOrWhiteSpace(cursorState))
            {
                request.AddParameter("cursorState", cursorState, ParameterType.QueryString);
            }

            if (pageLimit != null && pageLimit.HasValue)
            {
                request.AddParameter("limit", pageLimit.Value, ParameterType.QueryString);
            }

            return await client.ExecuteAsync(request);
        }

        [HttpGet]
        [Route("api/aps/bim360/account/{accountId}/project/{projectId}/asset-categories")]
        public async Task<IActionResult> GetBIM360AssetCategoriesAsync(string accountId, string projectId, [FromQuery] string cursorState, [FromQuery] Nullable<int> pageLimit = null, [FromQuery] bool buildTree = false)
        {
            RestResponse categoriesResponse = await GetAssetCategoriesAsync(projectId.Replace("b.", string.Empty), cursorState, pageLimit);
            var categories = JsonConvert.DeserializeObject<PaginatedAssetCategories>(categoriesResponse.Content);

            if (buildTree == false)
            {
                return Ok(categories.Results);
            }
            else
            {
                var root = categories.Results.FirstOrDefault();
                var tree = AssetCategory.BuildTree(categories.Results, root.Id);
                return Ok(tree);
            }
        }

        private async Task<RestResponse> GetAssetCategoriesAsync(string projectId, string cursorState, Nullable<int> pageLimit = null)
        {
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/bim360/assets/v1/projects/{project_id}/categories", RestSharp.Method.Get);
            request.AddParameter("project_id", projectId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);

            if (!string.IsNullOrWhiteSpace(cursorState))
            {
                request.AddParameter("cursorState", cursorState, ParameterType.QueryString);
            }

            if (pageLimit != null && pageLimit.HasValue)
            {
                request.AddParameter("limit", pageLimit.Value, ParameterType.QueryString);
            }

            return await client.ExecuteAsync(request);
        }

        [HttpGet]
        [Route("api/aps/bim360/account/{accountId}/project/{projectId}/asset-statuses")]
        public async Task<IActionResult> GetBIM360AssetStatusesAsync(string accountId, string projectId, [FromQuery] string cursorState, [FromQuery] Nullable<int> pageLimit = null)
        {
            RestResponse statusesResponse = await GetAssetStatusessAsync(projectId, cursorState, pageLimit);
            var statuses = JsonConvert.DeserializeObject<PaginatedAssetStatuses>(statusesResponse.Content);

            return Ok(statuses.Results);
        }

        private async Task<RestResponse> GetAssetStatusessAsync(string projectId, string cursorState, Nullable<int> pageLimit = null)
        {
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/bim360/assets/v1/projects/{project_id}/status-step-sets", RestSharp.Method.Get);
            request.AddParameter("project_id", projectId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);

            if (!string.IsNullOrWhiteSpace(cursorState))
            {
                request.AddParameter("cursorState", cursorState, ParameterType.QueryString);
            }

            if (pageLimit != null && pageLimit.HasValue)
            {
                request.AddParameter("limit", pageLimit.Value, ParameterType.QueryString);
            }

            return await client.ExecuteAsync(request);
        }

        // [HttpGet]
        // [Route("api/aps/bim360/account/{accountId}/project/{projectId}/locations")]
        // public async Task<IActionResult> GetBIM360LocationsAsync(string accountId, string projectId, [FromQuery] bool buildTree = false)
        // {
        //     RestResponse locsResponse = await GetLocationsAsync(accountId, projectId);
        //     var locations = JsonConvert.DeserializeObject<PaginatedLocations>(locsResponse.Content);

        //     if (buildTree == false)
        //     {
        //         return Ok(locations.Results);
        //     }
        //     else
        //     {
        //         var root = locations.Results.FirstOrDefault();
        //         var tree = Location.BuildTree(locations.Results, root.Id);
        //         return Ok(tree);
        //     }
        // }

        private async Task<string> GetContainerIdAsync(string accountId, string projectId, ContainerType type)
        {
            Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);

            ProjectsApi projectsApi = new ProjectsApi();
            projectsApi.Configuration.AccessToken = credentials.TokenInternal;
            var project = await projectsApi.GetProjectAsync(accountId, projectId);
            var relationships = project.data.relationships;
            string containerId = string.Empty;

            var result = relationships.Dictionary;

            foreach (var relation in result)
            {
                string name = relation.Key;
                if (name != type.Value)
                    continue;

                var data = relation.Value.data;
                if (data == null || !data.type.Contains(type.Value))
                    continue;

                containerId = data.id;
            }

            return containerId;
        }

        // private async Task<RestResponse> GetLocationsAsync(string accountId, string projectId)
        // {
        //     var containerId = await GetContainerIdAsync(accountId, projectId, ContainerType.Locations);

        //     Credentials credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);

        //     RestClient client = new RestClient(BASE_URL);
        //     RestRequest request = new RestRequest("/bim360/locations/v2/containers/{container_id}/trees/{tree_id}/nodes", RestSharp.Method.Get);
        //     request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
        //     request.AddParameter("tree_id", "default", ParameterType.UrlSegment);
        //     request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);

        //     return await client.ExecuteAsync(request);
        // }
    }
}