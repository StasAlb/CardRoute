using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CardRouteControl.ViewModel
{
    class MyPerso : INotifyPropertyChanged
    {
        private string ip;
        public string Ip
        {
            get { return ip; }
            set { ip = value; RaisePropertyChanged("ip"); }
        }

        private string port;

        public string Port
        {
            get { return port; }
            set { port = value; RaisePropertyChanged("port"); }
        }

        private string log;

        public string Log
        {
            get { return log; }
            set { log = value; RaisePropertyChanged("log"); }
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
