﻿//------------------------------------------------------------------------------
// <auto-generated>
//     Этот код создан программой.
//     Исполняемая версия:4.0.30319.42000
//
//     Изменения в этом файле могут привести к неправильной работе и будут потеряны в случае
//     повторной генерации кода.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Web.Services;
using System.Web.Services.Protocols;
using System.Xml.Serialization;
using System.ServiceModel;
using System.ServiceModel.Configuration;

namespace CwHubWebService
{
    //создание класса из wsdl
    //C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6.1 Tools\x64>wsdl /out:src /sharetypes FILE:///CardKB.wsdl /serverinterface

    
    [ServiceContract(Namespace = "urn:kubankredit:cardkb")]
    public interface IBindingInterface
    {
        
        [OperationContract]
        [FaultContract(typeof(errorResponse))]
        printResponse print([System.Xml.Serialization.XmlElementAttribute(Namespace = "urn:kubankredit:cardkb")] printRequest printRequest);

        
        [OperationContract]
        [FaultContract(typeof(errorResponse))]
        checkResponse check([System.Xml.Serialization.XmlElementAttribute(Namespace = "urn:kubankredit:cardkb")] checkRequest checkRequest);

        
        [OperationContract]
        [FaultContract(typeof(errorResponse))]
        closeResponse close([System.Xml.Serialization.XmlElementAttribute(Namespace = "urn:kubankredit:cardkb")] closeRequest closeRequest);
    }

    // при сериализации сюда параметры должны приходить в алфавитном порядке, иначе надо регулировать порядок [DataMember(Order = 1)]

    [DataContract(Name = "printRequest")]
    public partial class printRequest
    {
        [DataMember]
        public string Pan
        {
            get;set;
        }
        
        [DataMember]
        public string SeqNum
        {
            get;set;
        }
        
        [DataMember]
        public string FirstName
        {
            get; set;
        }
        
        [DataMember]
        public string SecondName
        {
            get;
            set;
        }
        
        [DataMember]
        public string EmbossedName
        {
            get;
            set;
        }

        
        [DataMember]
        public string CompanyName
        {
            get;
            set;
        }

        
        [DataMember]
        public System.DateTime ExpDate
        {
            get;
            set;
        }

        
        [DataMember]
        public System.DateTime StartDate
        {
            get;
            set;
        }

        
        [DataMember]
        public string ProductId
        {
            get;
            set;
        }

        
        [DataMember]
        public string PinIP
        {
            get;
            set;
        }

        
        [DataMember]
        public string EmbAlias
        {
            get;
            set;
        }
        [DataMember]
        public string DivisionName
        {
            get;
            set;
        }
    }
    [DataContract(Name = "printResponse")]
    public partial class printResponse
    {
        [DataMember]
        public int Status
        {
            get;
            set;
        }
        [DataMember]
        public string uIID
        {
            get;
            set;
        }
    }
    [DataContract(Name = "checkRequest")]
    public partial class checkRequest
    {
        [DataMember]
        public string uIID
        {
            get;
            set;
        }
    }
    [DataContract(Name = "checkResponse")]
    public partial class checkResponse
    {
        [DataMember]
        public int Status
        {
            get;
            set;
        }
    }
    [DataContract(Name= "closeRequest")]
    public partial class closeRequest
    {
        [DataMember]
        public string uIID
        {
            get;
            set;
        }
    }
    [DataContract(Name = "closeResponse")]
    public partial class closeResponse
    {
        [DataMember]
        public int Status
        {
            get;
            set;
        }
    }
    [DataContract(Name="errorResponse")]
    public partial class errorResponse
    {
        [DataMember]
        public int ErrCode
        {
            get;
            set;
        }
        [DataMember]
        public string ErrText
        {
            get;
            set;
        }
    }
}