﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Makaretu.Dns
{
    /// <summary>
    ///   Base class for an Internet address.
    /// </summary>
    public abstract class AddressRecord : ResourceRecord
    {
        /// <summary>
        ///   Creates a new instance of the <see cref="AddressRecord"/> class.
        /// </summary>
        public AddressRecord() : base()
        {
            TTL = ResourceRecord.DefaultHostTTL;
        }

        /// <summary>
        ///   The Internet address.
        /// </summary>
        /// <value>
        ///   Either IPv4 or IPv6.
        /// </value>
        public IPAddress Address { get; set; }

        /// <summary>
        ///   Creates an A or AAAA record based on the <see cref="AddressFamily"/>.
        /// </summary>
        /// <param name="name">
        ///   The name of the node that owns the address.
        /// </param>
        /// <param name="address">
        ///   An IPv4 or IPv6 address.
        /// </param>
        /// <returns>
        ///   An <see cref="ARecord"/> or <see cref="AAAARecord"/> tha describes
        ///   the <paramref name="name"/> and <paramref name="address"/>.
        /// </returns>
        public static AddressRecord Create(string name, IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return new ARecord { Name = name, Address = address };
            }

            return new AAAARecord { Name = name, Address = address };
        }

        /// <inheritdoc />
        public override void ReadData(DnsReader reader, int length)
        {
            Address = reader.ReadIPAddress(length);
        }

        /// <inheritdoc />
        public override void ReadData(MasterReader reader)
        {
            Address = reader.ReadIPAddress();
        }

        /// <inheritdoc />
        public override void WriteData(DnsWriter writer)
        {
            writer.WriteIPAddress(Address);
        }

        /// <inheritdoc />
        public override void WriteData(TextWriter writer)
        {
            writer.Write(Address.ToString());
        }
    }
}