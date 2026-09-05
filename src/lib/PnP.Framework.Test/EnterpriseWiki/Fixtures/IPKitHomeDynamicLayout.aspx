<%@ Page language="C#" Inherits="Microsoft.SharePoint.Publishing.PublishingLayoutPage" %>
<script type="text/javascript">
function loadjsfile(filename) {
    var fileref = document.createElement('script');
    fileref.setAttribute("type", "text/javascript");
    fileref.setAttribute("src", filename);
    document.getElementsByTagName("head")[0].appendChild(fileref);
}
function environmentContext() { return "ipkit"; }
loadjsfile("https://source.example/sites/" + environmentContext() + "/SiteAssets/Scripts/HomeIPKitBanner.js");
loadjsfile("https://source.example/sites/" + environmentContext() + "/SiteAssets/Scripts/HomeRelatedProjects.js");
loadjsfile("https://source.example/sites/" + environmentContext() + "/SiteAssets/Scripts/HomeIPKitQuickstart.js");
</script>
