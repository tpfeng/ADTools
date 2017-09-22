﻿Imports System.Collections.ObjectModel
Imports System.DirectoryServices
Imports System.Security.Principal
Imports System.Threading

Public Class clsSearcher
    Private basicsearchtasks As New List(Of Task)
    Private basicsearchtaskscts As New CancellationTokenSource
    Public Event BasicSearchAsyncDataRecieved()

    Private tombstonesearchtasks As New List(Of Task)
    Private tombstonesearchtaskscts As New CancellationTokenSource

    Sub New()

    End Sub

    Public Async Function BasicSearchStopAsync() As Task
        basicsearchtaskscts.Cancel()
        If basicsearchtasks.Count > 0 Then
            Try
                Await Task.WhenAll(basicsearchtasks.ToArray) 'magic
            Catch
            End Try

            basicsearchtasks.Clear()
            Log("Список задач очищен")
        End If
    End Function

    Public Async Function BasicSearchAsync(returncollection As clsThreadSafeObservableCollection(Of clsDirectoryObject),
                                           Optional parentobject As clsDirectoryObject = Nothing,
                                           Optional specificdomains As ObservableCollection(Of clsDomain) = Nothing,
                                           Optional attributes As ObservableCollection(Of clsAttribute) = Nothing,
                                           Optional pattern As String = Nothing,
                                           Optional searchobjectclasses As clsSearchObjectClasses = Nothing,
                                           Optional freesearch As Boolean = False
                                           ) As Task
        Await BasicSearchStopAsync()

        returncollection.Clear()

        basicsearchtaskscts = New CancellationTokenSource

        Dim roots As List(Of clsDirectoryObject)
        Dim searchscope As SearchScope

        If parentobject IsNot Nothing Then
            roots = New List(Of clsDirectoryObject) From {parentobject}
            searchscope = SearchScope.OneLevel
        Else
            roots = If(specificdomains Is Nothing, domains.Select(Function(d As clsDomain) New clsDirectoryObject(d.DefaultNamingContext, d)).ToList, specificdomains.Select(Function(d As clsDomain) New clsDirectoryObject(d.DefaultNamingContext, d)).ToList)
            searchscope = SearchScope.Subtree
        End If

        For Each root In roots
            Dim mt = Task.Factory.StartNew(
                Function()
                    Return BasicSearchSync(root, attributes, pattern, searchobjectclasses, freesearch, searchscope, basicsearchtaskscts.Token)
                End Function, basicsearchtaskscts.Token)
            basicsearchtasks.Add(mt)
            Log(String.Format("Задача на поиск в домене ""{0}"" создана", root.name))

            Dim lt = mt.ContinueWith(
                Function(parenttask As Task(Of ObservableCollection(Of clsDirectoryObject)))
                    basicsearchtasks.Remove(mt)
                    Log(String.Format("Вывод {0} результатов в домене ""{1}""", parenttask.Result.Count, If(parenttask.Result.Count > 0, parenttask.Result(0).Domain.Name, "null")))
                    For Each result In parenttask.Result
                        If basicsearchtaskscts.Token.IsCancellationRequested Then Return False
                        returncollection.Add(result)
                        Thread.Sleep(50)
                    Next
                    Log("Задача на поиск закрыта")
                    Return True
                End Function)
            basicsearchtasks.Add(lt)
            Log(String.Format("Задача на вывод результатов домена ""{0}"" создана", root.name))

            Dim ft = lt.ContinueWith(
                Function(parenttask As Task(Of Boolean))
                    Try
                        basicsearchtasks.Remove(lt)
                    Catch
                        basicsearchtasks.Clear()
                    End Try
                    Log("Задача на вывод результатов закрыта")
                    Return True
                End Function)
        Next

        Await Task.WhenAny(basicsearchtasks.ToArray)
        RaiseEvent BasicSearchAsyncDataRecieved()
        Await Task.WhenAll(basicsearchtasks.ToArray)
    End Function

    Public Function BasicSearchSync(Optional root As clsDirectoryObject = Nothing,
                                    Optional attributes As ObservableCollection(Of clsAttribute) = Nothing,
                                    Optional pattern As String = Nothing,
                                    Optional searchobjectclasses As clsSearchObjectClasses = Nothing,
                                    Optional freesearch As Boolean = False,
                                    Optional searchscope As SearchScope = SearchScope.Subtree,
                                    Optional ct As CancellationToken = Nothing) As ObservableCollection(Of clsDirectoryObject)

        If root Is Nothing Then Return New ObservableCollection(Of clsDirectoryObject)

        Dim results As New ObservableCollection(Of clsDirectoryObject)

        Try
            Dim ldapsearcher As New DirectorySearcher(root.Entry)
            ldapsearcher.SearchScope = searchscope
            ldapsearcher.PropertiesToLoad.Add("objectGuid")
            ldapsearcher.PageSize = 1000

            If searchscope = SearchScope.Subtree Then
                If String.IsNullOrEmpty(pattern) Then Return New ObservableCollection(Of clsDirectoryObject)

                Dim patterns() As String = pattern.Split({"/", vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                attributes = If(attributes, attributesForSearchDefault)
                searchobjectclasses = If(searchobjectclasses, New clsSearchObjectClasses)
                Dim attrfilter = ""
                attributes.ToList.ForEach(Sub(a As clsAttribute) patterns.ToList.ForEach(Sub(p) attrfilter &= String.Format("({0}={1}{2}*)", a.Name, If(freesearch, "*", ""), Trim(p))))
                Dim filter As String = "(&" +
                                            "(|" +
                                                If(searchobjectclasses.User, "(&(objectCategory=person)(!(objectClass=inetOrgPerson)))", "") +
                                                If(searchobjectclasses.Computer, "(objectCategory=computer)", "") +
                                                If(searchobjectclasses.Group, "(objectCategory=group)", "") +
                                                If(searchobjectclasses.Container, "(objectCategory=container)", "") +
                                            ")" +
                                            If(Not String.IsNullOrEmpty(attrfilter), "(|" & attrfilter & ")", "") +
                                        ")"
                ldapsearcher.Filter = "(thumbnailPhoto=*)"
            End If

            Dim ldapresults As SearchResultCollection = ldapsearcher.FindAll()

            For Each ldapresult As SearchResult In ldapresults
                If Not ct = Nothing AndAlso ct.IsCancellationRequested Then Return New ObservableCollection(Of clsDirectoryObject)

                Dim de As DirectoryEntry = ldapresult.GetDirectoryEntry
                de.RefreshCache(propertiesToLoadDefault)
                results.Add(New clsDirectoryObject(de, root.Domain))
            Next
            ldapresults.Dispose()

        Catch ex As Exception
            ThrowException(ex, "BasicSearchSync")
        End Try

        Return results
    End Function

    'Public Async Function TombstoneSearchAsync(returncollection As clsThreadSafeObservableCollection(Of clsDeletedDirectoryObject), pattern As String,
    '                                  Optional specificdomains As ObservableCollection(Of clsDomain) = Nothing,
    '                                  Optional freesearch As Boolean = False) As Task

    '    tombstonesearchtaskscts.Cancel()
    '    If tombstonesearchtasks.Count > 0 Then Exit Function
    '    returncollection.Clear()
    '    tombstonesearchtaskscts = New CancellationTokenSource

    '    Dim domainlist As ObservableCollection(Of clsDomain) = If(specificdomains Is Nothing, domains, specificdomains)

    '    For Each dmn In domainlist

    '        Dim mt = Task.Factory.StartNew(
    '            Function()
    '                Return TombstoneSearchSync(pattern, dmn, freesearch, tombstonesearchtaskscts.Token)
    '            End Function, tombstonesearchtaskscts.Token)
    '        tombstonesearchtasks.Add(mt)

    '        Dim lt = mt.ContinueWith(
    '            Function(parenttask As Task(Of ObservableCollection(Of clsDeletedDirectoryObject)))
    '                tombstonesearchtasks.Remove(mt)
    '                For Each result In parenttask.Result
    '                    If tombstonesearchtaskscts.Token.IsCancellationRequested Then Return False
    '                    returncollection.Add(result)
    '                    Thread.Sleep(30)
    '                Next
    '                Return True
    '            End Function)
    '        tombstonesearchtasks.Add(lt)

    '        Dim ft = lt.ContinueWith(
    '            Function(parenttask As Task(Of Boolean))
    '                tombstonesearchtasks.Remove(lt)
    '                Return True
    '            End Function)

    '    Next

    '    Await Task.WhenAny(tombstonesearchtasks.ToArray)
    'End Function

    'Public Function TombstoneSearchSync(pattern As String,
    '                                  domain As clsDomain,
    '                                  Optional freesearch As Boolean = False,
    '                                  Optional ct As CancellationToken = Nothing) As ObservableCollection(Of clsDeletedDirectoryObject)

    '    Dim properties As String() = {"cn",
    '                                  "distinguishedName",
    '                                  "lastKnownParent",
    '                                  "name",
    '                                  "objectClass",
    '                                  "objectGUID",
    '                                  "objectSID",
    '                                  "sAMAccountName",
    '                                  "userAccountControl",
    '                                  "whenChanged",
    '                                  "whenCreated"}

    '    If domain.Validated = False Then Return New ObservableCollection(Of clsDeletedDirectoryObject)

    '    Dim results As New ObservableCollection(Of clsDeletedDirectoryObject)

    '    Try
    '        Dim patterns() As String = pattern.Split({"/", vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)

    '        For Each singlepattern As String In patterns
    '            If ct.IsCancellationRequested Then Return New ObservableCollection(Of clsDeletedDirectoryObject)

    '            singlepattern = Trim(singlepattern)
    '            If String.IsNullOrEmpty(singlepattern) Then Continue For

    '            If singlepattern.StartsWith("""") And singlepattern.EndsWith("""") And Len(singlepattern) > 2 Then
    '                singlepattern = Mid(singlepattern, 2, Len(singlepattern) - 2)
    '            Else
    '                singlepattern = If(freesearch, "*" & singlepattern & "*", singlepattern & "*")
    '            End If

    '            Dim filter As String = "(&" +
    '                                        "(|" +
    '                                            "(&(objectClass=person)(!(objectClass=inetOrgPerson)))" +
    '                                            "(objectClass=computer)" +
    '                                            "(objectClass=group)" +
    '                                            "(objectClass=organizationalUnit)" +
    '                                        ")" +
    '                                        "(|" +
    '                                            "(name=" & singlepattern & ")" +
    '                                        ")" +
    '                                        "(isDeleted=TRUE)" +
    '                                     ")"

    '            Dim ldapsearcher As New DirectorySearcher(domain.SearchRoot)
    '            ldapsearcher.PropertiesToLoad.AddRange(properties)
    '            ldapsearcher.Filter = filter
    '            ldapsearcher.PageSize = 1000
    '            ldapsearcher.Tombstone = True

    '            Dim ldapresults As SearchResultCollection = ldapsearcher.FindAll()
    '            For Each ldapresult As SearchResult In ldapresults
    '                If ct.IsCancellationRequested Then Return New ObservableCollection(Of clsDeletedDirectoryObject)
    '                results.Add(New clsDeletedDirectoryObject(ldapresult, domain))
    '            Next
    '            ldapresults.Dispose()
    '        Next

    '    Catch ex As Exception
    '        ThrowException(ex, domain.Name)
    '    End Try

    '    Return results
    'End Function

End Class