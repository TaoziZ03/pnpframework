(async () => {
  const cases = [
    {
      caseId: 'ccd35-03',
      pageUrl: 'https://microsoft.sharepoint.com/teams/mswikis-gbi/Getfit/Getfit%20Program/Home.aspx'
    },
    {
      caseId: 'ccd35-06',
      pageUrl: 'https://microsoft.sharepoint.com/teams/campusipkits/industryipkitcybersecurity/Pages/Settings.aspx'
    },
    {
      caseId: 'ccd35-08',
      pageUrl: 'https://microsoft.sharepoint.com/teams/office_rdx/rm/SitePages/Mac%20Office%20Release%20Wiki.aspx'
    }
  ];
  const results = await Promise.all(cases.map(async item => {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 10000);
    try {
      const response = await fetch(item.pageUrl, {
        method: 'HEAD',
        headers: { Accept: 'text/html' },
        credentials: 'same-origin',
        cache: 'no-store',
        redirect: 'manual',
        signal: controller.signal
      });
      return {
        caseId: item.caseId,
        operation: 'authenticated-source-version-head',
        sourceMutationCount: 0,
        status: response.status,
        finalUrl: response.url,
        requestGuid: response.headers.get('sprequestguid'),
        etag: response.headers.get('etag'),
        lastModified: response.headers.get('last-modified'),
        contentLength: response.headers.get('content-length'),
        redirectLocation: response.headers.get('location'),
        versionSignal: [
          response.headers.get('etag') || '',
          response.headers.get('last-modified') || '',
          response.headers.get('content-length') || ''
        ].join('|')
      };
    } catch (error) {
      return {
        caseId: item.caseId,
        operation: 'authenticated-source-version-head',
        sourceMutationCount: 0,
        status: 0,
        error: error?.name === 'AbortError' ? 'timeout' : String(error)
      };
    } finally {
      clearTimeout(timeout);
    }
  }));
  return {
    schema: 'ccd103.source-version-recheck/v1',
    observedAtUtc: new Date().toISOString(),
    sourceHost: 'microsoft.sharepoint.com',
    mode: 'authenticated-read-only',
    sourceMutationCount: 0,
    results
  };
})()
