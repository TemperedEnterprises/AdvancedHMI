﻿Option Strict On
'***********************************************************************
'* Omron Host Link Com
'*
'* Copyright 2011 Archie Jacobs
'*
'* Reference : Omron W342-E1-15 (W342-E1-15+CS-CJ-CP-NSJ+RefManual.pdf)
'* Revision February 2010
'*
'***********************************************************************
'Imports OmronDriver.Common
Namespace Omron
    Public Class OmronEthernetFINSCom
        Inherits Omron.FINSBaseCom
        Implements MfgControl.AdvancedHMI.Drivers.IComComponent

        Private Shared DLL As List(Of MfgControl.AdvancedHMI.Drivers.Omron.FinsEthernetDataLinkLayer)


#Region "Properties"
        Private m_IPAddress As String = "192.168.0.1"
        <System.ComponentModel.Category("Communication Settings")> _
        Public Property IPAddress() As String
            Get
                Return m_IPAddress
            End Get
            Set(ByVal value As String)
                m_IPAddress = value

                CreateDLLInstance()

                If DLL.Count > 0 AndAlso DLL(MyDLLInstance) IsNot Nothing Then
                    DLL(MyDLLInstance).IPAddress = value
                End If
            End Set
        End Property

        Public Enum ProtocolOptions
            TCP
            UDP
        End Enum
        Private m_ProtocolType As ProtocolOptions
        Public Property ProtocolType As ProtocolOptions
            Get
                Return m_ProtocolType
            End Get
            Set(value As ProtocolOptions)
                If m_ProtocolType <> value Then
                    m_ProtocolType = value

                    If DLL IsNot Nothing AndAlso DLL.Count > 0 AndAlso DLL(MyDLLInstance) IsNot Nothing Then
                        If m_ProtocolType = ProtocolOptions.UDP Then
                            DLL(MyDLLInstance).ProtocolType = Net.Sockets.ProtocolType.Udp
                        Else
                            DLL(MyDLLInstance).ProtocolType = Net.Sockets.ProtocolType.Tcp
                        End If
                    End If
                End If
            End Set
        End Property

#End Region

#Region "Constructor"
        Public Sub New()
            MyBase.new()

            If DLL Is Nothing Then
                DLL = New List(Of MfgControl.AdvancedHMI.Drivers.Omron.FinsEthernetDataLinkLayer)
            End If

            TargetAddress = New MfgControl.AdvancedHMI.Drivers.Omron.DeviceAddress

            '* Version 3.99b - added to fix problem with running 2 HMIs
            '* Get the last byte of the IP address to use as the unit number
            Dim MyIP As System.Net.IPAddress = MfgControl.AdvancedHMI.Drivers.ADSUtilities.GetIPv4Address("", m_IPAddress)
            Dim b() As Byte = MyIP.GetAddressBytes
            Dim Unit As Byte
            If b.Length > 3 Then
                Unit = b(3)
            End If
            '* default port 1 (&HFC)
            SourceAddress = New MfgControl.AdvancedHMI.Drivers.Omron.DeviceAddress(0, &HFB, Unit)

            m_ProtocolType = ProtocolOptions.UDP
        End Sub

        Public Sub New(ByVal container As System.ComponentModel.IContainer)
            MyClass.New()

            'Required for Windows.Forms Class Composition Designer support
            container.Add(Me)
        End Sub


        Protected Overrides Sub Dispose(ByVal disposing As Boolean)
            MyBase.Dispose(disposing)

            '* The handle linked to the DataLink Layer has to be removed, otherwise it causes a problem when a form is closed
            RemoveDLLConnection(MyDLLInstance)
        End Sub


        Private Sub RemoveDLLConnection(ByVal instance As Integer)
            '* The handle linked to the DataLink Layer has to be removed, otherwise it causes a problem when a form is closed
            If DLL IsNot Nothing AndAlso DLL.Count > instance Then
                If DLL(instance) IsNot Nothing Then
                    RemoveHandler DLL(instance).DataReceived, AddressOf DataLinkLayerDataReceived
                    RemoveHandler DLL(instance).ComError, AddressOf DataLinkLayerComError
                    EventHandlerDLLInstance = 0

                    DLL(instance).ConnectionCount -= 1

                    If DLL(instance).ConnectionCount <= 0 Then
                        DLL(instance).dispose()
                        DLL(instance) = Nothing
                        DLL.Remove(DLL(MyDLLInstance))
                    End If
                End If
            End If
        End Sub

#End Region


#Region "Private Methods"
        '***************************************************************
        '* Create the Data Link Layer Instances
        '* if the IP Address is the same, then resuse a common instance
        '***************************************************************
        Protected Overrides Sub CreateDLLInstance()
            'If Me.DesignMode Then Exit Sub
            '*** For Windows CE port, this checks designmode and works in full .NET also***
            If AppDomain.CurrentDomain.FriendlyName.IndexOf("DefaultDomain", System.StringComparison.CurrentCultureIgnoreCase) >= 0 Then
                Exit Sub
            End If

            If DLL.Count > 0 Then
                '* At least one DLL instance already exists,
                '* so check to see if it has the same IP address
                '* if so, reuse the instance, otherwise create a new one
                Dim i As Integer
                While i < DLL.Count AndAlso DLL(i) IsNot Nothing AndAlso DLL(i).IPAddress <> m_IPAddress
                    i += 1
                End While
                MyDLLInstance = i
            End If

            If MyDLLInstance >= DLL.Count Then
                Dim NewDLL As New MfgControl.AdvancedHMI.Drivers.Omron.FinsEthernetDataLinkLayer(m_IPAddress)
                If m_ProtocolType = ProtocolOptions.UDP Then
                    NewDLL.ProtocolType = Net.Sockets.ProtocolType.Udp
                Else
                    NewDLL.ProtocolType = Net.Sockets.ProtocolType.Tcp
                End If
                DLL.Add(NewDLL)
            End If

            '* Have we already attached event handler to this data link layer?
            If EventHandlerDLLInstance <> (MyDLLInstance + 1) Then
                '* If event handler to another layer has been created, remove them
                If EventHandlerDLLInstance > 0 Then
                    RemoveHandler DLL(EventHandlerDLLInstance).DataReceived, AddressOf DataLinkLayerDataReceived
                    RemoveHandler DLL(EventHandlerDLLInstance).ComError, AddressOf DataLinkLayerComError
                End If

                AddHandler DLL(MyDLLInstance).DataReceived, AddressOf DataLinkLayerDataReceived
                AddHandler DLL(MyDLLInstance).ComError, AddressOf DataLinkLayerComError
                EventHandlerDLLInstance = MyDLLInstance + 1
            End If
        End Sub


        Friend Overrides Function SendData(ByVal FinsF As MfgControl.AdvancedHMI.Drivers.Omron.FINSFrame) As Boolean
            '* If a Subscription (Internal Request) begin to overflow the que, ignore some
            '* This can occur from too fast polling
            If DLL(MyDLLInstance).SendQueDepth < 10 Then
                DLL(MyDLLInstance).SendData(FinsF)
                Return True
            Else
                Return False
            End If
        End Function

        Protected Overrides Function GetNextTransactionID(ByVal maxValue As Integer) As Integer
            If DLL.Count > MyDLLInstance AndAlso DLL(MyDLLInstance) IsNot Nothing Then
                Return DLL(MyDLLInstance).GetNextTransactionNumber(maxValue)
            Else
                Return 0
            End If
        End Function
#End Region
    End Class
End Namespace
