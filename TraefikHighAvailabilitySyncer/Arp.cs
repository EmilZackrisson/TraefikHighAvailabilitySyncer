using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using SharpPcap;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace TraefikHighAvailabilitySyncer;

public class Arp(IConfiguration config, ILogger logger)
{
    private readonly string _virtualIp = config.GetValue<string>("VirtualIp")
        ?? throw new InvalidOperationException("VirtualIp is not configured");
    private readonly string _interfaceName = config.GetValue<string>("InterfaceName") 
        ?? throw new InvalidOperationException("InterfaceName is not configured");
    private readonly string _macAddress = config.GetValue<string>("MacAddress")
        ?? throw new InvalidOperationException("MacAddress is not configured");

    public void SendArp()
    {
        try
        {
            var devices = CaptureDeviceList.Instance;

            // Replace this with your actual interface name or index
            var device = devices.FirstOrDefault(d => d.Description.Contains(_interfaceName));
            if (device == null)
            {
                logger.LogCritical("ARP: Device not found. Please check your configuration.");
                return;
            }

            device.Open();

            // Local IP and MAC
            var senderIp = IPAddress.Parse(_virtualIp);
            var senderMac = PhysicalAddress.Parse(_macAddress.Replace(":", ""));

            // GARP: Target IP is same as sender IP
            var arpPacket = new ArpPacket(ArpOperation.Request,
                PhysicalAddress.Parse("00:00:00:00:00:00"),
                senderIp,
                senderMac,
                senderIp);

            // Ethernet frame
            var ethernetPacket = new EthernetPacket(senderMac,
                PhysicalAddress.Parse("FF:FF:FF:FF:FF:FF"),
                EthernetType.Arp);

            ethernetPacket.PayloadPacket = arpPacket;

            // Send packet
            device.SendPacket(ethernetPacket);

            logger.LogInformation("Gratuitous ARP sent from {SenderIp} ({SenderMac})", senderIp, senderMac);
            device.Close();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to send gratuitous ARP");
        }
    }
}