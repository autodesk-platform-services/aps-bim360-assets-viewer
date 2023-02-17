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

var viewer = null;

function launchViewer(urn, viewableId) {
  if (viewer != null) {
    viewer.tearDown()
    viewer.finish()
    viewer = null
    $("#apsViewer").empty();
  }
  var options = {
    env: 'AutodeskProduction',
    api: 'derivativeV2' + (atob(urn.replace('_', '/')).indexOf('emea') > -1 ? '_EU' : ''),
    // env: 'AutodeskProduction2',
    // api: 'streamingV2' + (atob(urn.replace('urn:', '').replace('_', '/')).indexOf('emea') > -1 ? '_EU' : ''),
    getAccessToken: getApsToken
  };

  Autodesk.Viewing.Initializer(options, () => {
    const config3d = {
      extensions: ['BIM360AssetExtension']
    };
    viewer = new Autodesk.Viewing.GuiViewer3D(document.getElementById('apsViewer'), config3d);
    viewer.start();
    var documentId = 'urn:' + urn;
    Autodesk.Viewing.Document.load(documentId, onDocumentLoadSuccess, onDocumentLoadFailure);
  });
  async function onDocumentLoadSuccess(doc) {
    await doc.downloadAecModelData();
    var viewables = (viewableId ? doc.getRoot().findByGuid(viewableId) : doc.getRoot().getDefaultGeometry());
    viewer.loadDocumentNode(doc, viewables).then(i => {

    });
  }

  function onDocumentLoadFailure(viewerErrorCode) {
    console.error('onDocumentLoadFailure() - errorCode:' + viewerErrorCode);
  }
}



function getApsToken(callback) {
  jQuery.ajax({
    url: '/api/aps/oauth/token',
    success: function (res) {
      callback(res.access_token, res.expires_in)
    }
  });
}
