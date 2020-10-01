using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CardRouteControl.ViewModel
{
    class MyCdp : INotifyPropertyChanged
    {
        private string cdpConsole;
        public string CdpConsole
        {
            get { return cdpConsole; }
            set { cdpConsole = value; RaisePropertyChanged("CdpConsole"); }
        }

        private string cdpIniFolder;

        public string CdpIniFolder
        {
            get { return cdpIniFolder; }
            set { cdpIniFolder = value; RaisePropertyChanged("CdpIniFolder"); }
        }

        private string cdpDefaultIni;

        public string CdpDefaultIni
        {
            get { return cdpDefaultIni; }
            set { cdpDefaultIni = value; RaisePropertyChanged("CdpDefaultIni"); }
        }

        private string cdpDefaultIn;

        public string CdpDefaultIn
        {
            get { return cdpDefaultIn; }
            set { cdpDefaultIn = value; RaisePropertyChanged("CdpDefaultIn"); }
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public void RaisePropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

    }
}
