using System.Xml.Linq;
using Tmds.DBus;
namespace FrameForge.Desktop;

[DBusInterface("org.freedesktop.DBus.Introspectable")]
public interface IPortalIntrospection : IDBusObject
{
    Task<string> IntrospectAsync();
}

internal static class PortalSupport
{
    internal static async Task Require(Connection connection,string name,string feature,CancellationToken cancel)
    {
        var proxy=connection.CreateProxy<IPortalIntrospection>("org.freedesktop.portal.Desktop","/org/freedesktop/portal/desktop");
        string xml;
        try { xml=await proxy.IntrospectAsync().WaitAsync(TimeSpan.FromSeconds(10),cancel); }
        catch(Exception e) when(e is DBusException or TimeoutException)
        {
            throw new InvalidOperationException("The Linux desktop sharing service is unavailable. Sign in to a desktop with its matching XDG portal backend, then try again.",e);
        }
        var document=XDocument.Parse(xml);
        if(!document.Root!.Elements("interface").Any(e=>(string?)e.Attribute("name")=="org.freedesktop.portal."+name))
            throw new InvalidOperationException("This Linux session does not provide "+feature+". Use a desktop with a compatible XDG portal backend. WSLg may support the editor without screen sharing or global shortcuts.");
    }
}
