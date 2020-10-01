using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CardRouteControl.ViewModel
{
    class MySqlServer : INotifyPropertyChanged
    {
        private DataTable serverNames;
        public DataTable ServerNames {
            get { return serverNames; }
            set
            {
                serverNames = value;
                RaisePropertyChanged("ServerNames");
            }
        }

        public DataView Servers
        {
            get
            {
                return serverNames?.DefaultView;
            }
        }
        public string serverName { get; set; }
        public string DbName { get; set; }
        public string Uid { get; set; }
        public string Pwd { get; set; }

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
