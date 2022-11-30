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

using System.Collections.Generic;

namespace bim360assets.Models
{
    /// <summary>
    /// PaginatedAssets
    /// </summary>
    public class PaginatedAssets
    {
        public PaginatedAssets()
        {
            this.Results = new List<Asset>();
        }

        /// <summary>
        /// Page info
        /// </summary>
        public Pagination Pagination { get; set; }

        /// <summary>
        /// An array of assets
        /// </summary>
        public List<Asset> Results { get; set; }
    }
}