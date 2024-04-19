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
    public partial class BIM360Controller : Controller
    {
        [HttpGet]
        [Route("api/aps/bim360/container")]
        public async Task<dynamic> GetContainerAsync(string href)
        {
            string[] idParams = href.Split('/');
            string projectId = idParams[idParams.Length - 1];
            string hubId = idParams[idParams.Length - 3];

            var project = await _aps.GetProject(hubId, projectId, this._tokens);
            var issues = project.Data.Relationships.Issues.Data;
            if (issues.Type != "issueContainerId") return null;

            return new { ContainerId = issues.Id, HubId = hubId };
        }

        private async Task<RestResponse> GetIssuesAsync(string containerId, string resource, string urn)
        {
            urn = Encoding.UTF8.GetString(Convert.FromBase64String(urn));

            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/{resource}?filter[target_urn]={urn}", RestSharp.Method.Get);
            request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
            request.AddParameter("urn", urn, ParameterType.UrlSegment);
            request.AddParameter("resource", resource, ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + this._tokens.InternalToken);
            return await client.ExecuteAsync(request);
        }

        [HttpGet]
        [Route("api/aps/bim360/account/{accountId}/container/{containerId}/issues/{urn}")]
        public async Task<JArray> GetDocumentIssuesAsync(string accountId, string containerId, string urn)
        {
            RestResponse documentIssuesResponse = await GetIssuesAsync(containerId, "quality-issues", urn);
            RestResponse usersResponse = await GetUsers(accountId);

            dynamic issues = JObject.Parse(documentIssuesResponse.Content);
            dynamic users = JArray.Parse(usersResponse.Content);
            foreach (dynamic issue in issues.data)
            {
                issue.attributes.assigned_to_name = "Not yet assigned"; // default value?
                foreach (dynamic user in users)
                {
                    if (user.uid == issue.attributes.assigned_to)
                    {
                        issue.attributes.assigned_to_name = user.name;
                    }
                }
            }

            return issues.data;
        }

        private async Task<RestResponse> PostIssuesAsync(string containerId, string resource, JObject data)
        {
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/{resource}", RestSharp.Method.Post);
            request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
            request.AddParameter("resource", resource, ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + this._tokens.InternalToken);
            request.AddHeader("Content-Type", "application/vnd.api+json");
            request.AddParameter("text/json", Newtonsoft.Json.JsonConvert.SerializeObject(data), ParameterType.RequestBody);

            return await client.ExecuteAsync(request);
        }

        private async Task<RestResponse> GetIssueTypesAsync(string containerId)
        {
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/ng-issue-types?include=subtypes", RestSharp.Method.Get);
            request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + this._tokens.InternalToken);
            request.AddHeader("Content-Type", "application/vnd.api+json");

            return await client.ExecuteAsync(request);
        }


        [HttpPost]
        [Route("api/aps/bim360/container/{containerId}/issues/{urn}")]
        public async Task<IActionResult> CreateDocumentIssuesAsync(string containerId, string urn, [FromBody] JObject data)
        {
            // for this sample, let's create Design issues
            // so we need the ngType and ngSubtype
            RestResponse issueTypesResponse = await GetIssueTypesAsync(containerId);
            dynamic issueTypes = JObject.Parse(issueTypesResponse.Content);
            string ngTypeId = string.Empty;
            string ngSubtypeId = string.Empty;
            foreach (dynamic ngType in issueTypes.results)
            {
                if (ngType.title == "Design") // ngType we're looking for
                {
                    foreach (dynamic subType in ngType.subtypes)
                    {
                        if (subType.title == "Design") // ngSubtype we're looking for
                        {
                            ngSubtypeId = subType.id; break; // stop looping subtype...
                        }
                    }
                    ngTypeId = ngType.id; break; // stop looping type...
                }
            }
            // double check we got it
            if (string.IsNullOrWhiteSpace(ngTypeId) || string.IsNullOrWhiteSpace(ngSubtypeId)) return BadRequest();
            // and replace on the payload
            data["data"]["attributes"]["ng_issue_type_id"] = ngTypeId;
            data["data"]["attributes"]["ng_issue_subtype_id"] = ngSubtypeId;

            // now post to Quality-Issues
            RestResponse documentIssuesResponse = await PostIssuesAsync(containerId, "quality-issues", data);

            return (documentIssuesResponse.StatusCode == HttpStatusCode.Created ? (IActionResult)Ok() : (IActionResult)BadRequest(documentIssuesResponse.Content));
        }
    }
}