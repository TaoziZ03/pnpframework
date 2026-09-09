(() => {
  const navigation = performance.getEntriesByType('navigation')[0];
  return {
    schema: 'ccd103.source-page-version-signal/v1',
    observedAtUtc: new Date().toISOString(),
    operation: 'authenticated-source-page-read',
    sourceMutationCount: 0,
    readyState: document.readyState,
    title: document.title,
    canonicalUrl: location.origin + location.pathname,
    documentLastModified: document.lastModified || null,
    navigation: navigation ? {
      name: navigation.name,
      responseStatus: navigation.responseStatus || null,
      transferSize: navigation.transferSize,
      encodedBodySize: navigation.encodedBodySize,
      decodedBodySize: navigation.decodedBodySize
    } : null
  };
})()
