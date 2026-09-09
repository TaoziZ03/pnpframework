(async () => {
  const urls = [
    '/_api/web?$select=Id,Title,ServerRelativeUrl,WebTemplate,Configuration',
    '/teams/campusipkits/_api/web?$select=Id,Title,ServerRelativeUrl,WebTemplate,Configuration',
    '/teams/mswikis-gbi/_api/web?$select=Id,Title,ServerRelativeUrl,WebTemplate,Configuration',
    '/teams/office_rdx/_api/web?$select=Id,Title,ServerRelativeUrl,WebTemplate,Configuration'
  ];
  const results = await Promise.all(urls.map(async url => {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 15000);
    try {
      const response = await fetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json;odata=nometadata' },
        credentials: 'same-origin',
        cache: 'no-store',
        signal: controller.signal
      });
      const text = await response.text();
      let body = null;
      try { body = JSON.parse(text); } catch { body = null; }
      return {
        url,
        status: response.status,
        requestGuid: response.headers.get('sprequestguid'),
        body: response.ok ? body : null,
        error: response.ok ? null : text.slice(0, 500)
      };
    } catch (error) {
      return { url, status: 0, error: error?.name === 'AbortError' ? 'timeout' : String(error) };
    } finally {
      clearTimeout(timeout);
    }
  }));
  return {
    schema: 'ccd103.target-admission-read/v1',
    observedAtUtc: new Date().toISOString(),
    targetOrigin: location.origin,
    mutationCount: 0,
    results
  };
})()
