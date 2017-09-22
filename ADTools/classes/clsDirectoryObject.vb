﻿Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.DirectoryServices
Imports System.Security.Principal

Public Class clsDirectoryObject
    Inherits Dynamic.DynamicObject
    Implements INotifyPropertyChanged

    Public Event PropertyChanged(sender As Object, e As System.ComponentModel.PropertyChangedEventArgs) Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged

    Private _entry As DirectoryEntry
    Private _searchresult As SearchResult
    Private _domain As clsDomain
    Private _children As New ObservableCollection(Of clsDirectoryObject)
    Private _childcontainers As New ObservableCollection(Of clsDirectoryObject)

    Private _properties As New Dictionary(Of String, Object)

    Private _manager As clsDirectoryObject
    Private _employees As ObservableCollection(Of clsDirectoryObject)
    Private _memberOf As ObservableCollection(Of clsDirectoryObject)
    Private _member As ObservableCollection(Of clsDirectoryObject)

    Private Sub NotifyPropertyChanged(propertyName As String)
        Me.OnPropertyChanged(New PropertyChangedEventArgs(propertyName))
    End Sub

    Public Sub NotifyMoved()
        NotifyPropertyChanged("distinguishedName")
        NotifyPropertyChanged("distinguishedNameFormated")
    End Sub

    Public Sub NotifyRenamed()
        NotifyPropertyChanged("name")
    End Sub

    Protected Overridable Sub OnPropertyChanged(e As PropertyChangedEventArgs)
        RaiseEvent PropertyChanged(Me, e)
    End Sub

    Sub New(Entry As DirectoryEntry, ByRef Domain As clsDomain)
        Me.Entry = Entry
        _domain = Domain
    End Sub

    Sub New(SearchResult As SearchResult, ByRef Domain As clsDomain)
        Me.SearchResult = SearchResult
        _domain = Domain
    End Sub

    Public Property Entry() As DirectoryEntry
        Get
            Return _entry
        End Get
        Set(ByVal value As DirectoryEntry)
            Try
                Dim o As Object = value.NativeObject
                _entry = value
            Catch
                _entry = Nothing
            End Try

            NotifyPropertyChanged("Entry")
        End Set
    End Property

    Public Property SearchResult() As SearchResult
        Get
            Return _searchresult
        End Get
        Set(ByVal value As SearchResult)
            _searchresult = value
            Entry = _searchresult.GetDirectoryEntry

            NotifyPropertyChanged("SearchResult")
        End Set
    End Property

    Public ReadOnly Property Domain() As clsDomain
        Get
            Return _domain
        End Get
    End Property


    'Public ReadOnly Property Children() As ObservableCollection(Of clsDirectoryObject)
    '    Get
    '        _children.Clear()
    '        If Entry Is Nothing Then Return _childcontainers

    '        Dim ds As New DirectorySearcher(Entry)
    '        ds.Tombstone = True
    '        ds.PropertiesToLoad.AddRange({"name", "objectClass"})
    '        ds.SearchScope = SearchScope.OneLevel

    '        For Each sr As SearchResult In ds.FindAll()
    '            _children.Add(New clsDirectoryObject(sr, Domain))
    '        Next

    '        Return _children
    '    End Get
    'End Property

    Public ReadOnly Property ChildContainers() As ObservableCollection(Of clsDirectoryObject)
        Get
            _childcontainers.Clear()
            If Entry Is Nothing Then Return _childcontainers

            Dim ds As New DirectorySearcher(Entry)
            ds.PropertiesToLoad.AddRange({"name", "objectClass"})
            ds.SearchScope = SearchScope.OneLevel
            ds.Filter = "(|(objectClass=organizationalUnit)(objectClass=container))"
            For Each sr As SearchResult In ds.FindAll()
                _childcontainers.Add(New clsDirectoryObject(sr, Domain))
            Next
            Return _childcontainers
        End Get
    End Property

    Public ReadOnly Property CanWrite(name As String) As Boolean
        Get
            Return AllowedAttributesEffective.Contains(name, StringComparer.OrdinalIgnoreCase)
        End Get
    End Property

    Public ReadOnly Property IsReadOnly(name As String) As Boolean
        Get
            Return Not AllowedAttributesEffective.Contains(name, StringComparer.OrdinalIgnoreCase)
        End Get
    End Property

    Public ReadOnly Property HasValue(name As String) As Boolean
        Get
            Return LdapProperty(name) IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property AllowedAttributes() As List(Of String)
        Get
            If LdapProperty("allowedAttributes") Is Nothing Then Entry.RefreshCache({"allowedAttributes"})

            Dim a As Object = LdapProperty("allowedAttributes")
            If IsArray(a) Then
                Return New List(Of String)(CType(a, Object()).Select(Function(x As Object) x.ToString).ToArray)
            ElseIf a Is Nothing Then
                Return New List(Of String)
            Else
                Return New List(Of String)({a.ToString})
            End If
        End Get
    End Property

    Public ReadOnly Property AllowedAttributesEffective() As List(Of String)
        Get
            If LdapProperty("allowedAttributesEffective") Is Nothing Then Entry.RefreshCache({"allowedAttributesEffective"})

            Dim a As Object = LdapProperty("allowedAttributesEffective")
            If IsArray(a) Then
                Return New List(Of String)(CType(a, Object()).Select(Function(x As Object) x.ToString).ToArray)
            ElseIf a Is Nothing Then
                Return New List(Of String)
            Else
                Return New List(Of String)({a.ToString})
            End If
        End Get
    End Property

    Public ReadOnly Property AllAttributes As ObservableCollection(Of clsAttribute)
        Get
            Dim aa As New ObservableCollection(Of clsAttribute)
            For Each attr As String In AllowedAttributes
                aa.Add(New clsAttribute(attr, "", Me.LdapProperty(attr)))
            Next
            Return aa
        End Get
    End Property

    Private Property LdapProperty(name As String) As Object
        Get
            If _properties.ContainsKey(name) Then Return _properties(name)

            Dim value As Object = Nothing
            Dim valuetyped As Object = Nothing

            If Entry IsNot Nothing Then
                Entry.RefreshCache({name})
                Dim pvc As PropertyValueCollection = Entry.Properties(name)
                If pvc.Value IsNot Nothing Then value = pvc.Value
            ElseIf SearchResult IsNot Nothing Then
                Dim rpvc As ResultPropertyValueCollection = SearchResult.Properties(name)
                If rpvc.Count > 0 AndAlso rpvc.Item(0) IsNot Nothing Then value = rpvc.Item(0)
            End If

            If value Is Nothing Then Return Nothing

            Select Case value.GetType()
                Case GetType(String)
                    valuetyped = value
                Case GetType(Integer)
                    valuetyped = value
                Case GetType(Byte())
                    valuetyped = value
                Case GetType(Object())
                    valuetyped = value
                Case GetType(DateTime)
                    valuetyped = value
                Case GetType(Boolean)
                    valuetyped = value
                Case Else 'System.__ComObject
                    Try
                        valuetyped = LongFromLargeInteger(value)
                    Catch
                        valuetyped = value
                    End Try
            End Select

            _properties.Add(name, valuetyped)

            Return valuetyped
        End Get
        Set(value As Object)
            Try
                If Entry Is Nothing Then Exit Property
                If IsNumeric(value) Or IsDate(value) Or Len(value) > 0 Then
                    Entry.Properties(name).Value = Trim(value)
                Else
                    Entry.Properties(name).Clear()
                End If

                Entry.CommitChanges()

                NotifyPropertyChanged(name)
            Catch ex As Exception
                ThrowException(ex, "Set LdapProperty")
            End Try
        End Set
    End Property

    Public Overrides Function TryGetMember(ByVal binder As Dynamic.GetMemberBinder, ByRef result As Object) As Boolean
        result = LdapProperty(binder.Name)
        Return True
    End Function

    Public Overrides Function TrySetMember(ByVal binder As Dynamic.SetMemberBinder, ByVal value As Object) As Boolean
        LdapProperty(binder.Name) = value
        NotifyPropertyChanged(binder.Name)
        Return True
    End Function

    'Default Public Property Attr(name As String) As Object
    '    Get
    '        Dim staticprops = Me.GetType.GetProperties()
    '        Dim props = Me.GetType.GetProperties().Where(Function(p) LCase(p.Name) = LCase(name))
    '        If props.Count = 1 Then
    '            Return props(0).GetValue(Me)
    '        Else
    '            Return LdapAttr(name)
    '        End If
    '    End Get
    '    Set(value)
    '        Dim staticprops = Me.GetType.GetProperties()
    '        Dim props = Me.GetType.GetProperties().Where(Function(p) LCase(p.Name) = LCase(name))
    '        If props.Count = 1 Then
    '            props(0).SetValue(Me, value)
    '        Else
    '            LdapAttr(name) = value
    '        End If
    '    End Set
    'End Property

    Public ReadOnly Property Status() As String
        Get
            Dim _statusFormated As String = ""

            If objectClass.Contains("user") Or objectClass.Contains("computer") Then
                If passwordNeverExpires Is Nothing Then
                    _statusFormated &= "Срок действия пароля неизвестен" & vbCr
                ElseIf passwordNeverExpires = False Then
                    If passwordExpiresDate() = Nothing Then
                        _statusFormated &= "Срок действия пароля неизвестен" & vbCr
                    ElseIf passwordExpiresDate() <= Now Then
                        _statusFormated &= "Срок действия пароля истек" & vbCr
                    End If
                End If

                If accountNeverExpires Is Nothing Then
                    _statusFormated &= "Срок действия объекта неизвестен" & vbCr
                ElseIf accountNeverExpires = False AndAlso accountExpiresDate <= Now Then
                    _statusFormated &= "Срок действия объекта истек" & vbCr
                End If

                If disabled Is Nothing Then
                    _statusFormated &= "Статус объекта неизвестен" & vbCr
                ElseIf disabled Then
                    _statusFormated &= "Объект заблокирован" & vbCr
                End If
                If _statusFormated.Length > 1 Then _statusFormated = _statusFormated.Remove(_statusFormated.Length - 1)
            End If

            Return _statusFormated
        End Get
    End Property

    Public ReadOnly Property ClassImage() As BitmapImage
        Get
            Dim _image As String = ""
            Dim oc As String() = objectClass

            If oc.Contains("user") Then
                _image = "images/user.ico"
            ElseIf oc.Contains("computer") Then
                _image = "images/computer.ico"
            ElseIf oc.Contains("group") Then
                _image = "images/group.ico"
            ElseIf oc.Contains("contact") Then
                _image = "images/contact.ico"
            ElseIf oc.Contains("domaindns") Then
                _image = "images/domain.ico"
            ElseIf oc.Contains("organizationalunit") Then
                _image = "images/organizationalunit.ico"
            ElseIf oc.Contains("container") Then
                _image = "images/container.ico"
            Else
                If name = "Deleted Objects" Then
                    _image = "images/container_deleted.ico"
                Else
                    _image = "images/object_unknown.ico"
                End If
            End If

            Return New BitmapImage(New Uri("pack://application:,,,/" & _image))
        End Get
    End Property

    Public ReadOnly Property Image() As BitmapImage
        Get
            Dim _image As String = ""

            If objectClass.Contains("user") Then
                _image = "images/user.ico"
                If passwordNeverExpires Is Nothing Then
                    _image = "images/user_expired.ico"
                ElseIf passwordNeverExpires = False Then
                    If passwordExpiresDate = Nothing Then
                        _image = "images/user_expired.ico"
                    ElseIf passwordExpiresDate() <= Now Then
                        _image = "images/user_expired.ico"
                    End If
                End If

                If accountNeverExpires Is Nothing Then
                    _image = "images/user_expired.ico"
                ElseIf accountNeverExpires = False AndAlso accountExpiresDate <= Now Then
                    _image = "images/user_expired.ico"
                End If

                If disabled Is Nothing Then
                    _image = "images/user_expired.ico"
                ElseIf disabled Then
                    _image = "images/user_blocked.ico"
                End If
            ElseIf objectClass.Contains("computer") Then
                _image = "images/computer.ico"
                If passwordNeverExpires Is Nothing Then
                    _image = "images/computer_expired.ico"
                ElseIf passwordNeverExpires = False Then
                    If passwordExpiresDate = Nothing Then
                        _image = "images/computer_expired.ico"
                    ElseIf passwordExpiresDate() <= Now Then
                        _image = "images/computer_expired.ico"
                    End If
                End If

                If accountNeverExpires Is Nothing Then
                    _image = "images/computer_expired.ico"
                ElseIf accountNeverExpires = False AndAlso accountExpiresDate <= Now Then
                    _image = "images/computer_expired.ico"
                End If

                If disabled Is Nothing Then
                    _image = "images/computer_expired.ico"
                ElseIf disabled Then
                    _image = "images/computer_blocked.ico"
                End If
            ElseIf objectClass.Contains("group") Then
                _image = "images/group.ico"
                If groupTypeSecurity Then
                    _image = "images/group.ico"
                ElseIf groupTypeDistribution Then
                    _image = "images/group_distribution.ico"
                Else
                    _image = "images/object_unknown.ico"
                End If
            ElseIf objectClass.Contains("contact") Then
                _image = "images/contact.ico"
            ElseIf objectClass.Contains("domaindns") Then
                _image = "images/domain.ico"
            ElseIf objectClass.Contains("organizationalunit") Then
                _image = "images/organizationalunit.ico"
            ElseIf objectClass.Contains("container") Then
                _image = "images/container.ico"
            Else
                If LdapProperty("name") = "Deleted Objects" Then
                    _image = "images/container_deleted.ico"
                Else
                    _image = "images/object_unknown.ico"
                End If
            End If


            Return New BitmapImage(New Uri("pack://application:,,,/" & _image))
        End Get
    End Property

    Public Sub ResetPassword()
        If Domain.DefaultPassword = "" Then Throw New Exception("Стандартный пароль в целевом домене не указан")

        _entry.Invoke("SetPassword", Domain.DefaultPassword)
        pwdLastSet = 0
        _entry.CommitChanges()
        description = String.Format("{0} {1} ({2})", "Пароль сброшен", Domain.Username, Now.ToShortTimeString & " " & Now.ToShortDateString)
    End Sub

    Public Sub SetPassword(password As String)
        _entry.Invoke("SetPassword", password)
        pwdLastSet = -1
        _entry.CommitChanges()
        description = String.Format("{0} {1} ({2})", "Пароль сброшен", Domain.Username, Now.ToShortTimeString & " " & Now.ToShortDateString)
    End Sub


    'cached ldap properties

#Region "User attributes"

    Public Property sn() As String
        Get
            Return If(LdapProperty("sn"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("sn") = value

            NotifyPropertyChanged("sn")
        End Set
    End Property

    Public Property initials() As String
        Get
            Return If(LdapProperty("initials"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("initials") = value

            NotifyPropertyChanged("initials")
        End Set
    End Property

    Public Property givenName() As String
        Get
            Return If(LdapProperty("givenName"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("givenName") = value

            NotifyPropertyChanged("givenName")
        End Set
    End Property

    Public Property displayName() As String
        Get
            Return If(LdapProperty("displayName"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("displayName") = value

            NotifyPropertyChanged("displayName")
        End Set
    End Property

    Public Property physicalDeliveryOfficeName() As String
        Get
            Return If(LdapProperty("physicalDeliveryOfficeName"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("physicalDeliveryOfficeName") = value

            NotifyPropertyChanged("physicalDeliveryOfficeName")
        End Set
    End Property

    Public Property company() As String
        Get
            Return If(LdapProperty("company"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("company") = value

            NotifyPropertyChanged("company")
        End Set
    End Property

    Public Property manager() As clsDirectoryObject
        Get
            If _manager Is Nothing Then
                Try
                    Dim managerDN As String = LdapProperty("manager")
                    If managerDN Is Nothing Then
                        _manager = Nothing
                    Else
                        _manager = New clsDirectoryObject(New DirectoryEntry("LDAP://" & Domain.Name & "/" & managerDN, Domain.Username, Domain.Password), Domain)
                    End If
                    Return _manager
                Catch ex As Exception
                    Return Nothing
                End Try
            Else
                Return _manager
            End If
        End Get
        Set(value As clsDirectoryObject)
            Dim path() As String = value.Entry.Path.Split({"/"}, StringSplitOptions.RemoveEmptyEntries)
            LdapProperty("manager") = path(UBound(path))
            _manager = value

            NotifyPropertyChanged("manager")
        End Set
    End Property

    Public Property telephoneNumber() As String
        Get
            Return If(LdapProperty("telephoneNumber"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("telephoneNumber") = value

            NotifyPropertyChanged("telephoneNumber")
        End Set
    End Property

    Public Property homePhone() As String
        Get
            Return If(LdapProperty("homePhone"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("homePhone") = value

            NotifyPropertyChanged("homePhone")
        End Set
    End Property

    Public Property ipPhone() As String
        Get
            Return If(LdapProperty("ipPhone"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("ipPhone") = value

            NotifyPropertyChanged("ipPhone")
        End Set
    End Property

    Public Property mobile() As String
        Get
            Return If(LdapProperty("mobile"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("mobile") = value

            NotifyPropertyChanged("mobile")
        End Set
    End Property

    Public Property streetAddress() As String
        Get
            Return If(LdapProperty("streetAddress"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("streetAddress") = value

            NotifyPropertyChanged("streetAddress")
        End Set
    End Property

    Public Property mail() As String
        Get
            Return If(LdapProperty("mail"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("mail") = value

            NotifyPropertyChanged("mail")
        End Set
    End Property

    Public Property title() As String
        Get
            Return If(LdapProperty("title"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("title") = value

            NotifyPropertyChanged("title")
        End Set
    End Property

    Public Property department() As String
        Get
            Return If(LdapProperty("department"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("department") = value

            NotifyPropertyChanged("department")
        End Set
    End Property

    Public Property userPrincipalName() As String
        Get
            Return If(LdapProperty("userPrincipalName"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("userPrincipalName") = value

            NotifyPropertyChanged("userPrincipalName")
        End Set
    End Property

    Public Property userPrincipalNameName() As String
        Get
            Dim s As String
            If userPrincipalName.Contains("@") Then
                s = Split(userPrincipalName, "@")(0)
            ElseIf Len(userPrincipalName) > 0 Then
                s = userPrincipalName
            Else
                s = ""
            End If
            Return s
        End Get
        Set(ByVal value As String)
            userPrincipalName = value & "@" & userPrincipalNameDomain

            NotifyPropertyChanged("userPrincipalNameName")
        End Set
    End Property

    Public Property userPrincipalNameDomain() As String
        Get
            Dim s As String
            If userPrincipalName.Contains("@") Then
                s = Split(userPrincipalName, "@")(1)
            Else
                s = ""
            End If
            Return s
        End Get
        Set(ByVal value As String)
            userPrincipalName = userPrincipalNameName & "@" & value

            NotifyPropertyChanged("userPrincipalNameDomain")
        End Set
    End Property

    Public Property memberOf() As ObservableCollection(Of clsDirectoryObject)
        Get
            If _memberOf Is Nothing Then
                Dim g As Object = LdapProperty("memberOf")
                If IsArray(g) Then          'если групп несколько
                    _memberOf = New ObservableCollection(Of clsDirectoryObject)(CType(g, Object()).Select(Function(x As Object) New clsDirectoryObject(New DirectoryEntry("LDAP://" + Domain.Name + "/" + x.ToString, Domain.Username, Domain.Password), Domain)).ToArray)
                ElseIf g Is Nothing Then    'если групп нет
                    _memberOf = New ObservableCollection(Of clsDirectoryObject)
                Else                        'если группа одна
                    _memberOf = New ObservableCollection(Of clsDirectoryObject)({New clsDirectoryObject(New DirectoryEntry("LDAP://" + Domain.Name + "/" + g.ToString, Domain.Username, Domain.Password), Domain)})
                End If
            End If
            Return _memberOf
        End Get
        Set(value As ObservableCollection(Of clsDirectoryObject))
            _memberOf = value

            NotifyPropertyChanged("memberOf")
        End Set
    End Property

    Public ReadOnly Property msExchHideFromAddressLists() As Boolean
        Get
            _entry.RefreshCache({"msExchHideFromAddressLists"})
            Return LdapProperty("msExchHideFromAddressLists")
        End Get
    End Property

    Public Property thumbnailPhoto() As Byte()
        Get
            If LdapProperty("thumbnailPhoto") IsNot Nothing Then
                Return LdapProperty("thumbnailPhoto")
            Else
                Dim ms As New IO.MemoryStream
                Application.GetResourceStream(New Uri("pack://application:,,,/images/user_image.png")).Stream.CopyTo(ms)
                Return ms.ToArray
            End If
        End Get
        Set(value As Byte())
            If value Is Nothing Then
                Try
                    _entry.Properties("thumbnailPhoto").Clear()
                    _entry.CommitChanges()
                Catch ex As Exception
                    ThrowException(ex, "Clear thumbnailPhoto")
                End Try
            Else
                Try
                    _entry.Properties("thumbnailPhoto").Clear()
                    _entry.Properties("thumbnailPhoto").Add(value)
                    _entry.CommitChanges()
                Catch ex As Exception
                    ThrowException(ex, "Set thumbnailPhoto")
                End Try
            End If

            NotifyPropertyChanged("thumbnailPhoto")
        End Set
    End Property

    Public Property userWorkstations() As String()
        Get
            Return If(LdapProperty("userWorkstations") IsNot Nothing, LdapProperty("userWorkstations").Split({","}, StringSplitOptions.RemoveEmptyEntries), New String() {})
        End Get
        Set(value As String())
            LdapProperty("userWorkstations") = Join(value, ",")

            NotifyPropertyChanged("userWorkstations")
        End Set
    End Property

#End Region

#Region "Computer attributes"

    Public ReadOnly Property dNSHostName As String
        Get
            Return If(LdapProperty("dNSHostName"), "")
        End Get
    End Property

    Public Property location() As String
        Get
            Return If(LdapProperty("location"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("location") = value

            NotifyPropertyChanged("location")
        End Set
    End Property

    Public ReadOnly Property operatingSystem() As String
        Get
            Return If(LdapProperty("operatingSystem"), "")
        End Get
    End Property

    Public ReadOnly Property operatingSystemVersion() As String
        Get
            Return If(LdapProperty("operatingSystemVersion"), "")
        End Get
    End Property

#End Region

#Region "Group attributes"

    Public Property groupType() As Long
        Get
            Return LdapProperty("groupType")
        End Get
        Set(ByVal value As Long)
            Try
                LdapProperty("groupType") = value
            Catch ex As Exception
                ShowWrongMemberMessage()
            End Try

            NotifyPropertyChanged("groupType")
            NotifyPropertyChanged("groupTypeScopeDomainLocal")
            NotifyPropertyChanged("groupTypeScopeDomainGlobal")
            NotifyPropertyChanged("groupTypeScopeDomainUniversal")
            NotifyPropertyChanged("groupTypeSecurity")
            NotifyPropertyChanged("groupTypeDistribution")
            NotifyPropertyChanged("Image")
        End Set
    End Property

    Public Property groupTypeScopeDomainLocal() As Boolean
        Get
            Return groupType And ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP
        End Get
        Set(ByVal value As Boolean)
            If value Then
                Dim gt As Long = groupType
                If Not (gt And ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP) Then
                    gt = gt + ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP
                End If
                If (gt And ADS_GROUP_TYPE_GLOBAL_GROUP) Then
                    gt = gt - ADS_GROUP_TYPE_GLOBAL_GROUP
                End If
                If (gt And ADS_GROUP_TYPE_UNIVERSAL_GROUP) Then
                    gt = gt - ADS_GROUP_TYPE_UNIVERSAL_GROUP
                End If
                groupType = gt
            End If
        End Set
    End Property

    Public Property groupTypeScopeDomainGlobal() As Boolean
        Get
            Return groupType And ADS_GROUP_TYPE_GLOBAL_GROUP
        End Get
        Set(ByVal value As Boolean)
            If value Then
                Dim gt As Long = groupType
                If (gt And ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP) Then
                    gt = gt - ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP
                End If
                If Not (gt And ADS_GROUP_TYPE_GLOBAL_GROUP) Then
                    gt = gt + ADS_GROUP_TYPE_GLOBAL_GROUP
                End If
                If (gt And ADS_GROUP_TYPE_UNIVERSAL_GROUP) Then
                    gt = gt - ADS_GROUP_TYPE_UNIVERSAL_GROUP
                End If
                groupType = gt
            End If
        End Set
    End Property

    Public Property groupTypeScopeDomainUniversal() As Boolean
        Get
            Return groupType And ADS_GROUP_TYPE_UNIVERSAL_GROUP
        End Get
        Set(ByVal value As Boolean)
            If value Then
                Dim gt As Long = groupType
                If (gt And ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP) Then
                    gt = gt - ADS_GROUP_TYPE_DOMAIN_LOCAL_GROUP
                End If
                If (gt And ADS_GROUP_TYPE_GLOBAL_GROUP) Then
                    gt = gt - ADS_GROUP_TYPE_GLOBAL_GROUP
                End If
                If Not (gt And ADS_GROUP_TYPE_UNIVERSAL_GROUP) Then
                    gt = gt + ADS_GROUP_TYPE_UNIVERSAL_GROUP
                End If
                groupType = gt
            End If
        End Set
    End Property

    Public Property groupTypeSecurity() As Boolean
        Get
            Return groupType And ADS_GROUP_TYPE_SECURITY_ENABLED
        End Get
        Set(ByVal value As Boolean)
            If value Then
                If Not (groupType And ADS_GROUP_TYPE_SECURITY_ENABLED) Then
                    groupType = groupType + ADS_GROUP_TYPE_SECURITY_ENABLED
                End If
            Else
                If (groupType And ADS_GROUP_TYPE_SECURITY_ENABLED) Then
                    groupType = groupType - ADS_GROUP_TYPE_SECURITY_ENABLED
                End If
            End If
        End Set
    End Property

    Public ReadOnly Property groupTypeDistribution() As Boolean
        Get
            Return Not groupTypeSecurity
        End Get
    End Property

    Public Property info() As String
        Get
            Return If(LdapProperty("info"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("info") = value

            NotifyPropertyChanged("info")
        End Set
    End Property

    Public Property member() As ObservableCollection(Of clsDirectoryObject)
        Get
            If _member Is Nothing Then
                Dim o As Object = LdapProperty("member")
                If IsArray(o) Then          'если объектов несколько
                    _member = New ObservableCollection(Of clsDirectoryObject)(CType(o, Object()).Select(Function(x As Object) New clsDirectoryObject(New DirectoryEntry("LDAP://" + Domain.Name + "/" + x.ToString, Domain.Username, Domain.Password), Domain)).ToArray)
                ElseIf o Is Nothing Then    'если объектов нет
                    _member = New ObservableCollection(Of clsDirectoryObject)
                Else                        'если объект один
                    _member = New ObservableCollection(Of clsDirectoryObject)({New clsDirectoryObject(New DirectoryEntry("LDAP://" + Domain.Name + "/" + o.ToString, Domain.Username, Domain.Password), Domain)})
                End If
            End If
            Return _member
        End Get
        Set(value As ObservableCollection(Of clsDirectoryObject))
            _member = value

            NotifyPropertyChanged("member")
        End Set
    End Property

#End Region

#Region "Shared attributes"

    Public ReadOnly Property objectClass() As String()
        Get
            Try
                Dim oc = LdapProperty("objectclass")
                If IsArray(oc) Then
                    Return CType(oc, Object()).Select(Function(x) LCase(x.ToString)).ToArray
                Else
                    Return New String() {oc}
                End If
            Catch ex As Exception
                Return Nothing
            End Try
        End Get
    End Property

    Public Property accountExpires() As Long?
        Get
            Return LdapProperty("accountExpires")
        End Get
        Set(ByVal value As Long?)
            If value IsNot Nothing Then LdapProperty("accountExpires") = value

            NotifyPropertyChanged("accountExpires")
            NotifyPropertyChanged("accountExpiresDate")
            NotifyPropertyChanged("accountExpiresFormated")
            NotifyPropertyChanged("accountExpiresFlag")
            NotifyPropertyChanged("accountNeverExpires")
        End Set
    End Property

    Public Property accountExpiresDate() As Date
        Get
            If accountExpires IsNot Nothing Then
                If accountExpires = 0 Or accountExpires = 9223372036854775807 Then
                    Return Nothing
                Else
                    Return Date.FromFileTime(accountExpires)
                End If
            Else
                Return Nothing
            End If
        End Get
        Set(ByVal value As Date)
            accountExpires = value.ToFileTime

            NotifyPropertyChanged("accountExpiresDate")
        End Set
    End Property

    Public ReadOnly Property accountExpiresFormated() As String
        Get
            If accountExpires IsNot Nothing Then
                If accountExpires = 0 Or accountExpires = 9223372036854775807 Then
                    Return "никогда"
                Else
                    Return Date.FromFileTime(accountExpires).ToString
                End If
            Else
                Return "неизвестно"
            End If
        End Get
    End Property

    Public Property accountNeverExpires() As Boolean?
        Get
            If accountExpires IsNot Nothing Then
                Return accountExpires = 0 Or accountExpires = 9223372036854775807
            Else
                Return Nothing
            End If
        End Get
        Set(ByVal value As Boolean?)
            If value IsNot Nothing Then
                accountExpires = If(value, 0, Now.ToFileTime)
                NotifyPropertyChanged("accountNeverExpires")
            End If
        End Set
    End Property

    Public ReadOnly Property accountExpiresAt() As Boolean?
        Get
            If accountNeverExpires IsNot Nothing Then
                Return Not accountNeverExpires
            Else
                Return Nothing
            End If
        End Get
    End Property

    Public ReadOnly Property badPwdCount() As Integer?
        Get
            Return LdapProperty("badPwdCount")
        End Get
    End Property

    Public Property description() As String
        Get
            Return If(LdapProperty("description"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("description") = value

            NotifyPropertyChanged("description")
        End Set
    End Property

    Public ReadOnly Property distinguishedName() As String
        Get
            Return If(LdapProperty("distinguishedName"), "")
        End Get
    End Property

    Public ReadOnly Property distinguishedNameFormated() As String
        Get
            Dim OU() As String = Split(distinguishedName, ",")
            Dim ResultString As String = ""
            For I As Integer = UBound(OU) To 0 Step -1
                If OU(I).StartsWith("OU") Then
                    ResultString = ResultString & " \ " & Mid(OU(I), 4)
                End If
            Next
            Return Mid(ResultString, 4)
        End Get
    End Property

    Public ReadOnly Property lastLogon() As Long?
        Get
            Return LdapProperty("lastLogon")
        End Get
    End Property

    Public ReadOnly Property lastLogonDate() As Date
        Get
            If lastLogon IsNot Nothing Then
                Return If(lastLogon <= 0, Nothing, Date.FromFileTime(lastLogon))
            Else
                Return Nothing
            End If
        End Get
    End Property

    Public ReadOnly Property lastLogonFormated() As String
        Get
            If lastLogon IsNot Nothing Then
                Return If(lastLogon <= 0, "никогда", Date.FromFileTime(lastLogon).ToString)
            Else
                Return "неизвестно"
            End If
        End Get
    End Property

    Public ReadOnly Property logonCount() As Integer
        Get
            Return LdapProperty("logonCount")
        End Get
    End Property

    Public ReadOnly Property name() As String
        Get
            Return If(LdapProperty("name"), "")
        End Get
    End Property

    Public ReadOnly Property objectGUID() As String
        Get
            Return New Guid(TryCast(LdapProperty("objectGUID"), Byte())).ToString
        End Get
    End Property

    Public ReadOnly Property objectSID() As String
        Get
            Try
                Dim sid As New SecurityIdentifier(TryCast(LdapProperty("objectSid"), Byte()), 0)
                If sid.IsAccountSid Then Return sid.ToString
                Return Nothing
            Catch
                Return Nothing
            End Try
        End Get
    End Property

    Public Property pwdLastSet() As Long?
        Get
            Return LdapProperty("pwdLastSet")
        End Get
        Set(ByVal value As Long?)
            If value IsNot Nothing Then LdapProperty("pwdLastSet") = value.ToString

            NotifyPropertyChanged("pwdLastSet")
            NotifyPropertyChanged("pwdLastSetDate")
            NotifyPropertyChanged("pwdLastSetFormated")
            NotifyPropertyChanged("passwordExpiresDate")
            NotifyPropertyChanged("passwordExpiresFormated")
            NotifyPropertyChanged("userMustChangePasswordNextLogon")
        End Set
    End Property

    Public ReadOnly Property pwdLastSetDate() As Date
        Get
            Return If(pwdLastSet IsNot Nothing AndAlso pwdLastSet > 0, Date.FromFileTime(pwdLastSet), Nothing)
        End Get
    End Property

    Public ReadOnly Property pwdLastSetFormated() As String
        Get
            Return If(pwdLastSet Is Nothing, "неизвестно", If(pwdLastSet = 0, "истек", Date.FromFileTime(pwdLastSet).ToString))
        End Get
    End Property

    Public ReadOnly Property passwordExpiresDate() As Date
        Get
            If passwordNeverExpires IsNot Nothing AndAlso passwordNeverExpires Then
                Return Nothing
            Else
                Return If(pwdLastSet IsNot Nothing AndAlso pwdLastSet > 0, Date.FromFileTime(pwdLastSet).AddDays(Domain.MaxPwdAge), Nothing)
            End If
        End Get
    End Property

    Public ReadOnly Property passwordExpiresFormated() As String
        Get
            If passwordNeverExpires IsNot Nothing AndAlso passwordNeverExpires Then
                Return "никогда"
            Else
                Return If(pwdLastSet Is Nothing, "неизвестно", If(pwdLastSet = 0, "истек", Date.FromFileTime(pwdLastSet).AddDays(Domain.MaxPwdAge).ToString))
            End If
        End Get
    End Property

    Public Property userMustChangePasswordNextLogon() As Boolean?
        Get
            If pwdLastSet IsNot Nothing Then
                Return Not CBool(pwdLastSet)
            Else
                Return Nothing
            End If
        End Get
        Set(ByVal value As Boolean?)
            If value IsNot Nothing Then
                If value Then
                    pwdLastSet = 0 'password expired
                Else
                    pwdLastSet = -1 'password changed today
                End If
            End If
        End Set
    End Property

    Public Property sAMAccountName() As String
        Get
            Return If(LdapProperty("sAMAccountName"), "")
        End Get
        Set(ByVal value As String)
            LdapProperty("sAMAccountName") = value

            NotifyPropertyChanged("sAMAccountName")
        End Set
    End Property

    Public Property userAccountControl() As Integer?
        Get
            Return LdapProperty("userAccountControl")
        End Get
        Set(ByVal value As Integer?)
            If value IsNot Nothing Then LdapProperty("userAccountControl") = value

            NotifyPropertyChanged("userAccountControl")
            NotifyPropertyChanged("normalAccount")
            NotifyPropertyChanged("disabled")
            NotifyPropertyChanged("disabledFormated")
            NotifyPropertyChanged("passwordNeverExpires")
            NotifyPropertyChanged("passwordExpiresDate")
            NotifyPropertyChanged("passwordExpiresFormated")
            NotifyPropertyChanged("Image")
        End Set
    End Property

    Public Property normalAccount() As Boolean?
        Get
            Return If(userAccountControl Is Nothing, Nothing, userAccountControl And ADS_UF_NORMAL_ACCOUNT)
        End Get
        Set(ByVal value As Boolean?)
            If value Is Nothing Then Exit Property
            If value Then
                If Not (userAccountControl And ADS_UF_NORMAL_ACCOUNT) Then
                    userAccountControl = userAccountControl + ADS_UF_NORMAL_ACCOUNT
                End If
            Else
                If (userAccountControl And ADS_UF_NORMAL_ACCOUNT) Then
                    userAccountControl = userAccountControl - ADS_UF_NORMAL_ACCOUNT
                End If
            End If
        End Set
    End Property

    Public Property disabled() As Boolean?
        Get
            Return If(userAccountControl Is Nothing, Nothing, userAccountControl And ADS_UF_ACCOUNTDISABLE)
        End Get
        Set(ByVal value As Boolean?)
            If value Is Nothing Then Exit Property
            If value Then
                If Not (userAccountControl And ADS_UF_ACCOUNTDISABLE) Then
                    userAccountControl = userAccountControl + ADS_UF_ACCOUNTDISABLE
                End If
            Else
                If (userAccountControl And ADS_UF_ACCOUNTDISABLE) Then
                    userAccountControl = userAccountControl - ADS_UF_ACCOUNTDISABLE
                End If
            End If
            description = String.Format("{0} {1} ({2})", If(value, "Заблокирован", "Разблокирован"), Domain.Username, Now.ToShortTimeString & " " & Now.ToShortDateString)
        End Set
    End Property

    Public ReadOnly Property disabledFormated() As String
        Get
            If disabled IsNot Nothing Then
                If disabled Then
                    Return "✓"
                Else
                    Return ""
                End If
            Else
                Return "неизвестно"
            End If
        End Get
    End Property

    Public Property passwordNeverExpires() As Boolean?
        Get
            Return If(userAccountControl Is Nothing, Nothing, userAccountControl And ADS_UF_DONT_EXPIRE_PASSWD)
        End Get
        Set(ByVal value As Boolean?)
            If value Is Nothing Then Exit Property
            If value Then
                If Not (userAccountControl And ADS_UF_DONT_EXPIRE_PASSWD) Then
                    userAccountControl = userAccountControl + ADS_UF_DONT_EXPIRE_PASSWD
                End If
            Else
                If (userAccountControl And ADS_UF_DONT_EXPIRE_PASSWD) Then
                    userAccountControl = userAccountControl - ADS_UF_DONT_EXPIRE_PASSWD
                End If
            End If
        End Set
    End Property

    Public ReadOnly Property whenCreated() As Date
        Get
            Return LdapProperty("whenCreated")
        End Get
    End Property

    Public ReadOnly Property whenCreatedFormated() As String
        Get
            Return If(whenCreated = Nothing, "неизвестно", whenCreated.ToString)
        End Get
    End Property

    Public ReadOnly Property whenChanged() As Date
        Get
            Return LdapProperty("whenChanged")
        End Get
    End Property

#End Region


End Class
