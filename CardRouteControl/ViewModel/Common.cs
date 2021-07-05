using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CardRouteControl.ViewModel
{
    class MyCommon :INotifyPropertyChanged
    {
        public string timeout { get; set; }
        public string language { get; set; }
        public string protocol { get; set; }
        public string updateFinal { get; set; }
        public string updateArchive { get; set; }
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
