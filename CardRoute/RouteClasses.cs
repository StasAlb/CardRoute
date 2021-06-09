using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Devices;

namespace CardRoute
{
    enum CardStatus : int
    {
        PrepWaiting = 1,
        PrepProcess = 2,
        PinWaiting = 3,
        PinProcess = 4,
        PrintWaiting = 5,
        PrintProcess = 6,
        ReportWaiting = 7,
        ReportProcess = 8,
        Error = 9,
        Complete = 10,
        Pause = 11,
        Start = 12,
        Central = 13,
        OperatorPending = 14,
        AdminPending = 15,
        IssueDispensing = 16, //вылезла из киоска и ждет пока ее возьмут
        CompleteCentral = 17,
        Cancel = 18 //у меня нигде не используется
    }

    enum DeviceType : int
    {
        None = 0,
        CD800 = 1,
        CE840 = 2,
        CE870 = 3,
        Central = 4
    }

    class Card
    {
        internal int cardId;
        internal string cardData;
        internal int productId;
        internal string productLink;
        internal int hopper;
        internal int deviceId;
        internal DeviceType deviceType;
        internal string deviceLink;
        internal string deviceName;
        internal string message;
        internal int branchid;
        internal int lastStatusId;
        internal static DeviceType GetDeviceType(int dType)
        {
            switch (dType)
            {
                case (int)DeviceType.CD800:
                    return DeviceType.CD800;
                case (int)DeviceType.CE840:
                    return DeviceType.CE840;
                case (int)DeviceType.CE870:
                    return DeviceType.CE870;
                default:
                    return DeviceType.None;
            }
        }
    }
}
