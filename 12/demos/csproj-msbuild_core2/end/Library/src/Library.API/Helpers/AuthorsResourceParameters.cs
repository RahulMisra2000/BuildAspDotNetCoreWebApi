using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Library.API.Helpers
{
// ****** This will be specified as a Controller's Action Parmater so that if the Http Request contains any key / value pairs whose key matches 
//        the public properties of this class then the value will be populated and the Controller's Action will have access toit.
    public class AuthorsResourceParameters
    {
        // For PAGING 
            const int maxPageSize = 20;
            public int PageNumber { get; set; } = 1;

            private int _pageSize = 10;
            public int PageSize
            {
                get
                {
                    return _pageSize;
                }
                set
                {
                    _pageSize = (value > maxPageSize) ? maxPageSize : value;
                }
            }

        // For FILTERING
            public string Genre { get; set; }

        // For SEACRHING
            public string SearchQuery { get; set; }

        // For SORTING
        //  ere we are giving it a default value
            public string OrderBy { get; set; } = "Name";

        // For DATA SHAPING
        // So, the client can specify a list of fields from the Authors table that they are interested in getting back 
        // I guess the client will send a comma separated list of field name they are interested in ...
        public string Fields { get; set; }
    }
}
